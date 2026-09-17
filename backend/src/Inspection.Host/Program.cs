using System.Text.Json;
using System.Text.Json.Serialization;
using Inspection.Infrastructure.Plc;
using Inspection.Application.Engineering;
using Inspection.Domain.Planning;

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

var app = builder.Build();
app.MapGet("/health/live", () => new { Status = "alive" });
app.MapGet("/api/system/status", () => new
{
    ArchitectureVersion = "1.3",
    Stage = "EngineeringFoundation",
    ProductionReady = false,
    UnavailableCapabilities = new[] { "TrayExecution", "DeviceIntegration", "Algorithms", "Traceability", "Desktop" }
});
app.MapGet("/api/engineering/demo-plan", (DemoPlanService service) => service.GetPlan());
app.Run();

public partial class Program { }

public sealed class DemoTrayConfiguration
{
    public string[] PartIds { get; set; } = [];
    public FaceDefinition[] Faces { get; set; } = [];
}
