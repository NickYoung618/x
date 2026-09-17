using System.Net;
using System.Text.Json;
using Inspection.Application.Motion;
using Inspection.Application.Workflow;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Inspection.Host.Tests;

public class HostContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;
    public HostContractTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
    }

    [Fact]
    public void Framework_host_has_no_bound_motion_or_acquisition_provider()
    {
        Assert.Null(factory.Services.GetService(typeof(IMotionDevice)));
        Assert.Null(factory.Services.GetService(typeof(IPlacementLocator)));
        Assert.Null(factory.Services.GetService(typeof(ICapturePort)));
        Assert.Null(factory.Services.GetService(typeof(IAlgorithmPort)));
    }

    [Fact]
    public async Task Live_does_not_claim_production_readiness()
    {
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("alive", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Status_explicitly_reports_unavailable_production_capabilities()
    {
        var response = await client.GetAsync("/api/system/status");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("productionReady").GetBoolean());
        Assert.Equal("1.3", json.RootElement.GetProperty("architectureVersion").GetString());
        Assert.Equal("V13FrameworkFoundation", json.RootElement.GetProperty("stage").GetString());
        var unavailable = json.RootElement.GetProperty("unavailableCapabilities").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("TrayExecution", unavailable);
        Assert.Contains("DeviceIntegration", unavailable);
        Assert.Contains("Algorithms", unavailable);
        Assert.Contains("Traceability", unavailable);
        Assert.Contains("Desktop", unavailable);
    }

    [Fact]
    public async Task Framework_status_lists_all_fifteen_modules_without_claiming_an_implemented_station()
    {
        var response = await client.GetAsync("/api/system/status");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("V13FrameworkFoundation", root.GetProperty("stage").GetString());
        Assert.Equal("Unconfigured", root.GetProperty("runtimeMode").GetString());
        Assert.False(root.GetProperty("productionReady").GetBoolean());
        var modules = root.GetProperty("modules").EnumerateArray().ToArray();
        Assert.Equal(15, modules.Length);
        var ids = modules.Select(m => m.GetProperty("id").GetString() ?? string.Empty)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var expected = new[] { "Presentation", "Api", "Jobs", "Recipes", "Workflow", "Motion", "Acquisition",
            "AlgorithmRuntime", "Quality", "Traceability", "Media", "DeviceAdapters", "Diagnostics",
            "ModelManagement", "Mes" }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, ids);
        Assert.DoesNotContain(modules, m => m.GetProperty("state").GetString() == "Implemented");
        var device = Assert.Single(modules, m => m.GetProperty("id").GetString() == "DeviceAdapters");
        Assert.Equal("LegacyEngineering", device.GetProperty("state").GetString());
        Assert.All(root.GetProperty("capabilities").EnumerateArray(), c => Assert.False(c.GetProperty("available").GetBoolean()));
    }

    [Fact]
    public async Task Unimplemented_prepare_command_fails_closed_with_stable_problem_code()
    {
        var response = await client.PostAsync("/api/jobs/prepare", null);
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CAPABILITY_NOT_IMPLEMENTED", json.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("correlationId").GetString()));
        using var after = JsonDocument.Parse(await client.GetStringAsync("/api/system/status"));
        Assert.False(after.RootElement.GetProperty("productionReady").GetBoolean());
    }

    [Fact]
    public async Task Demo_query_returns_eight_captures_without_executing_them()
    {
        var response = await client.GetAsync("/api/engineering/demo-plan");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("isTestFixture").GetBoolean());
        Assert.False(root.GetProperty("executionEnabled").GetBoolean());
        var faces = root.GetProperty("plan").GetProperty("faces").EnumerateArray().ToArray();
        Assert.Equal(2, faces.Length);
        Assert.Equal(8, faces.Sum(f => f.GetProperty("captures").GetArrayLength()));
        Assert.True(faces[1].GetProperty("requiresFlipBefore").GetBoolean());
        Assert.Equal(["A", "A", "B", "B"], faces[0].GetProperty("captures").EnumerateArray()
            .Select(c => c.GetProperty("camera").GetString()));
    }

    [Fact]
    public async Task Unimplemented_production_commands_are_not_accepted()
    {
        var response = await client.PostAsync("/api/jobs/start", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
