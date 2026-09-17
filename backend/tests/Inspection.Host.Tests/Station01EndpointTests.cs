using System.Net;
using System.Net.Http.Json;
using Inspection.Application.Motion;
using Inspection.Application.Workflow;
using Inspection.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inspection.Host.Tests;

public class Station01EndpointTests
{
    [Fact]
    public async Task Disabled_engineering_endpoint_cannot_start_a_station()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/engineering/station-01/prepare",
            new Station01PrepareDto("tray-1", "scene"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<OperationProblemDto>();
        Assert.Equal("ST01_ENGINEERING_DISABLED", problem!.Code);
    }

    [Fact]
    public async Task Enabled_endpoint_without_external_ports_fails_closed()
    {
        await using var factory = EngineeringFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/engineering/station-01/prepare",
            new Station01PrepareDto("tray-1", "scene"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ST01_PROVIDER_UNAVAILABLE",
            (await response.Content.ReadFromJsonAsync<OperationProblemDto>())!.Code);
    }

    [Fact]
    public async Task Engineering_endpoint_rejects_real_provider_before_readiness_or_motion()
    {
        var motion = new MotionPort();
        await using var factory = EngineeringFactory(services =>
        {
            services.AddSingleton<IReadyObserver, ReadyPort>();
            services.AddSingleton<IMotionDevice>(motion);
            services.AddSingleton<IPlacementLocator, PlacementPort>();
            services.AddSingleton<IStation01ProviderIdentity, RealIdentity>();
        });
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/engineering/station-01/prepare",
            new Station01PrepareDto("tray-real", "scene"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ST01_PROVIDER_MODE_MISMATCH",
            (await response.Content.ReadFromJsonAsync<OperationProblemDto>())!.Code);
        Assert.Equal(0, motion.SubmitCount);
    }

    [Fact]
    public async Task Bound_ports_run_one_station_and_duplicate_request_cannot_repeat_motion()
    {
        var motion = new MotionPort();
        var placement = new PlacementPort();
        await using var factory = EngineeringFactory(services =>
        {
            services.AddSingleton<IReadyObserver, ReadyPort>();
            services.AddSingleton<IMotionDevice>(motion);
            services.AddSingleton<IPlacementLocator>(placement);
            services.AddSingleton<IStation01ProviderIdentity, SyntheticIdentity>();
        });
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/engineering/station-01/prepare",
            new Station01PrepareDto("tray-1", "scene"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<Station01RunDto>();
        Assert.NotNull(run);
        Assert.Equal("WaitingTrayCode", run.Stage);
        Assert.Equal("synthetic", run.Source);
        Assert.Equal((1, 1, 1, false),
            (run.MoveRequests, run.ScanRequests, run.FRequests, run.FExecuted));
        Assert.Equal(["tray-1:s1", "tray-1:s2"], run.PartIds);
        Assert.Equal(1, motion.SubmitCount);
        Assert.Equal(1, placement.CallCount);

        var saved = await client.GetFromJsonAsync<Station01RunDto>(
            "/api/engineering/station-01/runs/tray-1");
        Assert.NotNull(saved);
        Assert.Equal(run.TrayRunId, saved.TrayRunId);
        Assert.Equal(run.Stage, saved.Stage);
        Assert.Equal(run.PendingRequestId, saved.PendingRequestId);
        Assert.Equal(run.PartIds, saved.PartIds);
        Assert.Equal(run.Trace.Select(t => t.Event), saved.Trace.Select(t => t.Event));
        var duplicate = await client.PostAsJsonAsync("/api/engineering/station-01/prepare",
            new Station01PrepareDto("tray-1", "scene"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, motion.SubmitCount);
    }

    [Fact]
    public async Task O01_diagnostic_failure_never_reports_success_or_repeats_motion()
    {
        var motion = new MotionPort();
        await using var factory = EngineeringFactory(services =>
        {
            services.AddSingleton<IReadyObserver, ReadyPort>();
            services.AddSingleton<IMotionDevice>(motion);
            services.AddSingleton<IPlacementLocator, PlacementPort>();
            services.AddSingleton<IStation01ProviderIdentity, SyntheticIdentity>();
            services.AddSingleton<ILogger<Program>>(new ThrowingLogger());
        });
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/engineering/station-01/prepare",
            new Station01PrepareDto("tray-audit", "scene"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ST01_DIAGNOSTIC_WRITE_FAILED",
            (await response.Content.ReadFromJsonAsync<OperationProblemDto>())!.Code);
        var run = await client.GetFromJsonAsync<Station01RunDto>(
            "/api/engineering/station-01/runs/tray-audit");
        Assert.Equal("RecoveryRequired", run!.Stage);
        Assert.Equal("ST01_DIAGNOSTIC_WRITE_FAILED", run.FailureCode);
        Assert.Equal(1, motion.SubmitCount);
        var retry = await client.PostAsJsonAsync("/api/engineering/station-01/prepare",
            new Station01PrepareDto("tray-audit", "scene"));
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(1, motion.SubmitCount);
    }

    private static WebApplicationFactory<Program> EngineeringFactory(
        Action<IServiceCollection>? configure = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Station01:EngineeringEnabled"] = "true" }));
            if (configure is not null) builder.ConfigureTestServices(configure);
        });

    private sealed class ReadyPort : IReadyObserver
    {
        public Task<DeviceReady> ObserveAsync(string trayRunId, CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceReady(trayRunId, true, true, true,
                DateTimeOffset.UtcNow, "synthetic"));
    }

    private sealed class SyntheticIdentity : IStation01ProviderIdentity
    {
        public Station01ProviderDescription Describe() =>
            new("synthetic", "s01-draft-0.1", "test-double");
    }

    private sealed class RealIdentity : IStation01ProviderIdentity
    {
        public Station01ProviderDescription Describe() =>
            new("real", "s01-draft-0.1", "physical-device");
    }

    private sealed class MotionPort : IMotionDevice
    {
        public int SubmitCount { get; private set; }
        public Task<MotionDeviceReceipt> SubmitAsync(RequestMotion request, CancellationToken cancellationToken)
        {
            SubmitCount++;
            return Task.FromResult(new MotionDeviceReceipt(true, null));
        }
        public Task<MotionDeviceCompletion> WaitForCompletionAsync(Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MotionDeviceCompletion(MotionOutcome.Completed, null));
    }

    private sealed class PlacementPort : IPlacementLocator
    {
        public int CallCount { get; private set; }
        public Task<PlacementScan> LocateAsync(RequestScan request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new PlacementScan(request.RequestId, new PlacementMetadata(request.TrayRunId,
                request.ProposedEpoch, request.ExpectedSource, "synthetic", "opaque", null,
                DateTimeOffset.UtcNow),
                [new LocatedSlot("s1", "position-1"), new LocatedSlot("s2", "position-2")]));
        }
    }

    private sealed class ThrowingLogger : ILogger<Program>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new IOException("diagnostic target unavailable");

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
