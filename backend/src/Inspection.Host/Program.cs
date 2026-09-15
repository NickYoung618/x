using System.Text.Json.Serialization;
using Inspection.Application.Engineering;
using Inspection.Domain.Planning;

var builder = WebApplication.CreateBuilder(args);
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
