using System.Text.Json;
using System.Text.Json.Serialization;
using Inspection.Infrastructure.Plc;
using Inspection.Application.Architecture;
using Inspection.Application.Engineering;
using Inspection.Application.Plc;
using Inspection.Contracts;
using Inspection.Domain.Planning;

var probeMode = args.Contains("--plc-probe", StringComparer.Ordinal);
var deviceCheckMode = args.Contains("--plc-device-check", StringComparer.Ordinal);
if (probeMode && deviceCheckMode) throw new ArgumentException("Choose one PLC engineering command.");
var builder = WebApplication.CreateBuilder(args.Where(a => a is not ("--plc-probe" or "--plc-device-check")).ToArray());
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
if (deviceCheckMode)
{
    var config = builder.Configuration;
    var options = config.GetSection("Plc").Get<PlcConnectionOptions>() ?? new PlcConnectionOptions();
    if (config.GetValue("Plc:EngineeringActionEnabled", false) ||
        !string.IsNullOrWhiteSpace(config["Plc:TestSequence"]))
        throw new ArgumentException("The 2026-09-11 engineering check is read-only until a complete motion contract is signed.");
    var reportPath = config["Plc:Report"] ?? throw new ArgumentException("Plc:Report is required.");
    await using var device = PlcDeviceFactory.Create(options);
    PlcSnapshot? snapshot = null;
    string outcome;
    string? error = null;
    try
    {
        snapshot = await device.ReadSnapshotAsync();
        outcome = "ReadOnly";
    }
    catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or InvalidOperationException)
    {
        outcome = "Unknown";
        error = $"{exception.GetType().Name}: {exception.Message}";
    }
    var result = new
    {
        device.Source, device.Contract, Outcome = outcome, Error = error, Snapshot = snapshot,
        Exchanges = (device as Protocol20260911PlcDevice)?.Exchanges
    };
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    }));
    Console.WriteLine($"PLC device check ({device.Source}): {outcome}. Report: {reportPath}");
    Environment.ExitCode = outcome == "ReadOnly" ? 0 : 2;
    return;
}
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://127.0.0.1:5000");
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var demo = builder.Configuration.GetRequiredSection("DemoTray").Get<DemoTrayConfiguration>()
    ?? throw new InvalidOperationException("DemoTray configuration is required.");
builder.Services.AddSingleton(new DemoPlanService(demo.PartIds, demo.Faces));
builder.Services.AddSingleton<IPlcDevice>(_ => PlcDeviceFactory.Create(
    builder.Configuration.GetSection("Plc").Get<PlcConnectionOptions>() ?? new PlcConnectionOptions()));

var app = builder.Build();
_ = app.Services.GetRequiredService<IPlcDevice>(); // validate the selected profile without connecting
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
app.MapGet("/api/engineering/demo-plan", (DemoPlanService service) => service.GetPlan());
app.Run();

public partial class Program { }

public sealed class DemoTrayConfiguration
{
    public string[] PartIds { get; set; } = [];
    public FaceDefinition[] Faces { get; set; } = [];
}
