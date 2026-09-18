using System.Text.Json;
using System.Text.Json.Serialization;
using Inspection.Infrastructure.Plc;
using Inspection.Application.Architecture;
using Inspection.Application.Engineering;
using Inspection.Application.Motion;
using Inspection.Application.Workflow;
using Inspection.Contracts;
using Inspection.Domain.Planning;
using System.Net;

var probeMode = args.Contains("--plc-probe", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--plc-probe").ToArray());
if (probeMode)
{
    var config = builder.Configuration;
    var contract = config["Plc:Contract"] ?? "";
    var host = config["Plc:Host"] ?? "";
    var port = config.GetValue<int>("Plc:Port");
    var ioTimeout = config.GetValue("Plc:IoTimeoutMs", 1000);
    var actionTimeout = config.GetValue("Plc:ActionTimeoutMs", 3000);
    var reportPath = config["Plc:Report"] ?? throw new ArgumentException("Plc:Report is required.");
    LegacyPlcProbe.ValidateEndpoint(contract, host, port, ioTimeout);
    await using var transport = new ModbusTcpClient(host, port, 1, TimeSpan.FromMilliseconds(ioTimeout));
    var probe = new LegacyPlcProbe(transport, actionTimeout);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var result = await probe.RunAsync(deadline.Token);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));
    Console.WriteLine($"Legacy PLC probe: {result.Outcome}. {result.Reason}");
    Environment.ExitCode = result.Outcome == "Completed" ? 0 : 2;
    return;
}
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5000");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var demo = builder.Configuration.GetRequiredSection("DemoTray").Get<DemoTrayConfiguration>()
    ?? throw new InvalidOperationException("DemoTray configuration is required.");
builder.Services.AddSingleton(new DemoPlanService(demo.PartIds, demo.Faces));
builder.Services.AddSingleton<Station01EngineeringGate>();

var app = builder.Build();
app.MapGet("/health/live", () => new { Status = "alive" });
app.MapGet("/api/system/status", () => new SystemStatusDto(
    V13Architecture.Version,
    V13Architecture.Stage,
    V13Architecture.ProductionReady,
    V13Architecture.RuntimeMode.ToString(),
    V13Architecture.Capabilities.Where(capability => !capability.Available).Select(capability => capability.Id).ToArray(),
    V13Architecture.Modules.Select(module => new ModuleStatusDto(
        module.Id, module.Layer, module.Owner, module.State.ToString())).ToArray(),
    V13Architecture.Capabilities.Select(capability => new CapabilityStatusDto(
        capability.Id, capability.Available, capability.EvidenceLevel, capability.Reason)).ToArray()));
app.MapPost("/api/jobs/prepare", (HttpContext context) =>
    Results.Json(new OperationProblemDto("CAPABILITY_NOT_IMPLEMENTED", "检测任务尚未接入，不能启动设备。",
        context.TraceIdentifier), statusCode: StatusCodes.Status501NotImplemented,
        contentType: "application/problem+json"));
app.MapPost("/api/engineering/station-01/prepare", async (
    Station01PrepareDto request, HttpContext context, IServiceProvider services,
    Station01EngineeringGate gate, ILogger<Program> logger) =>
{
    if (!EngineeringAllowed(context))
        return Problem("ST01_ENGINEERING_DISABLED", "首工位工程入口未启用或请求不来自本机。",
            StatusCodes.Status403Forbidden, context);
    if (string.IsNullOrWhiteSpace(request.TrayRunId) || string.IsNullOrWhiteSpace(request.ScenarioId) ||
        request.TrayRunId.Length > 128 || request.ScenarioId.Length > 128)
        return Problem("ST01_INVALID_REQUEST", "必须提供有效盘次和场景标识。",
            StatusCodes.Status400BadRequest, context);
    var ready = services.GetService<IReadyObserver>();
    var motion = services.GetService<IMotionDevice>();
    var placement = services.GetService<IPlacementLocator>();
    var identity = services.GetService<IStation01ProviderIdentity>();
    if (ready is null || motion is null || placement is null || identity is null)
        return Problem("ST01_PROVIDER_UNAVAILABLE", "独立设备接口尚未接入，首工位不能运行。",
            StatusCodes.Status503ServiceUnavailable, context);
    Station01ProviderDescription provider;
    try { provider = identity.Describe(); }
    catch (Exception) { provider = new Station01ProviderDescription("invalid", "", ""); }
    if (provider.Source != "synthetic" || string.IsNullOrWhiteSpace(provider.ContractVersion) ||
        string.IsNullOrWhiteSpace(provider.DeviceId))
        return Problem("ST01_PROVIDER_MODE_MISMATCH", "工程入口只接受已标识的独立合成设备接口。",
            StatusCodes.Status503ServiceUnavailable, context);
    if (!gate.TryStart(request.TrayRunId))
        return Problem("ST01_RUN_CONFLICT", "已有工位运行，或该盘次已使用；不得重复发送运动。",
            StatusCodes.Status409Conflict, context);

    Station01RunDto? response = null;
    try
    {
        var result = await new Station01Runner(ready, motion, placement)
            .RunAsync(request.TrayRunId, request.ScenarioId, context.RequestAborted);
        var snapshot = result.Snapshot;
        response = new Station01RunDto(snapshot.TrayRunId, snapshot.ScenarioId,
            snapshot.Stage.ToString(), snapshot.Source, snapshot.FailureCode, snapshot.Failure,
            snapshot.PendingRequestId, snapshot.CoordinateEpoch,
            snapshot.Parts.Select(part => part.PartId).ToArray(), result.MoveRequests,
            result.ScanRequests, result.FRequests, false,
            result.Trace.Select(item => new Station01TraceDto(item.AtUtc, item.Event,
                item.CorrelationId, item.Stage.ToString(), item.FailureCode, item.Detail)).ToArray());
        try
        {
            foreach (var entry in response.Trace)
                logger.LogInformation("S01 {RunId} {Event} {Stage} {CorrelationId} {FailureCode} {Source} {Detail}",
                    response.TrayRunId, entry.Event, entry.Stage, entry.CorrelationId,
                    entry.FailureCode, response.Source, entry.Detail);
        }
        catch (Exception)
        {
            response = response with
            {
                Stage = result.MoveRequests > 0 ? "RecoveryRequired" : "Failed",
                FailureCode = "ST01_DIAGNOSTIC_WRITE_FAILED",
                Failure = "关键诊断无法写入；已发动作须人工核对，禁止自动重试。"
            };
            return Problem(response.FailureCode, response.Failure,
                StatusCodes.Status503ServiceUnavailable, context);
        }
        return Results.Json(response);
    }
    catch (Exception error)
    {
        try { logger.LogError(error, "S01 engineering run {RunId} stopped unexpectedly", request.TrayRunId); }
        catch (Exception) { /* The error response must still fail closed if logging itself failed. */ }
        return Problem("ST01_RUN_ERROR", "首工位工程执行异常；该盘次禁止自动重试。",
            StatusCodes.Status503ServiceUnavailable, context);
    }
    finally { gate.Finish(request.TrayRunId, response); }
});
app.MapGet("/api/engineering/station-01/runs/{runId}", (
    string runId, HttpContext context, Station01EngineeringGate gate) =>
{
    if (!EngineeringAllowed(context))
        return Problem("ST01_ENGINEERING_DISABLED", "首工位工程入口未启用或请求不来自本机。",
            StatusCodes.Status403Forbidden, context);
    return gate.Get(runId) is { } result ? Results.Json(result) : Results.NotFound();
});
app.MapGet("/api/engineering/demo-plan", (DemoPlanService service) => service.GetPlan());
app.Run();

bool EngineeringAllowed(HttpContext context) =>
    app.Configuration.GetValue<bool>("Station01:EngineeringEnabled") &&
    (context.Connection.RemoteIpAddress is { } remote
        ? IPAddress.IsLoopback(remote)
        : app.Environment.IsEnvironment("Testing"));

static IResult Problem(string code, string title, int status, HttpContext context) =>
    Results.Json(new OperationProblemDto(code, title, context.TraceIdentifier),
        statusCode: status, contentType: "application/problem+json");

public partial class Program { }

public sealed class DemoTrayConfiguration
{
    public string[] PartIds { get; set; } = [];
    public FaceDefinition[] Faces { get; set; } = [];
}
