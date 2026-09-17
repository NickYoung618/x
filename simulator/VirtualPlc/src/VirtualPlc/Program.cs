using System.Net;
using VirtualPlc;

var builder = WebApplication.CreateBuilder(args);
var protocolProfile = builder.Configuration["Simulation:ProtocolProfile"] ?? "LegacyV6";
if (protocolProfile is not ("LegacyV6" or "Protocol20260911Synthetic"))
    throw new InvalidOperationException("Simulation:ProtocolProfile must be LegacyV6 or Protocol20260911Synthetic.");
if (protocolProfile == "Protocol20260911Synthetic" &&
    (!IPAddress.TryParse(builder.Configuration["Modbus:ListenAddress"], out var syntheticAddress) ||
     !IPAddress.IsLoopback(syntheticAddress)))
    throw new InvalidOperationException("The synthetic 2026-09-11 Modbus profile must listen on loopback.");
if (protocolProfile == "Protocol20260911Synthetic" &&
    (builder.Configuration["urls"] ?? "http://127.0.0.1:5080").Split(';')
        .Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                      !(uri.Host == "localhost" ||
                        IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address))))
    throw new InvalidOperationException("The synthetic 2026-09-11 management API must listen on loopback.");

builder.Services.Configure<ModbusOptions>(builder.Configuration.GetSection("Modbus"));
builder.Services.Configure<SimulationOptions>(builder.Configuration.GetSection("Simulation"));
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
if (protocolProfile == "LegacyV6")
{
    builder.Services.AddSingleton<PlcDataStore>();
    builder.Services.AddSingleton<IModbusDataStore>(serviceProvider =>
        serviceProvider.GetRequiredService<PlcDataStore>());
    builder.Services.AddSingleton<VirtualPlcEngine>();
    builder.Services.AddHostedService(serviceProvider =>
        serviceProvider.GetRequiredService<VirtualPlcEngine>());
}
else
{
    builder.Services.AddSingleton<Protocol20260911Store>();
    builder.Services.AddSingleton<IModbusDataStore>(serviceProvider =>
        serviceProvider.GetRequiredService<Protocol20260911Store>());
    builder.Services.AddSingleton<Protocol20260911Engine>();
    builder.Services.AddHostedService(serviceProvider =>
        serviceProvider.GetRequiredService<Protocol20260911Engine>());
}
builder.Services.AddHostedService<ModbusTcpServer>();

var app = builder.Build();

if (protocolProfile == "LegacyV6")
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "VirtualPlc",
    protocolProfile,
    timestamp = DateTimeOffset.UtcNow
}));

if (protocolProfile == "LegacyV6")
{
    app.MapGet("/api/simulator/state", (VirtualPlcEngine engine) =>
        Results.Ok(engine.GetSnapshot()));

    app.MapGet("/api/simulator/address-map", () => Results.Ok(new
    {
        addressConvention = "Document addresses are one-based; Modbus PDU offsets are zero-based.",
        coils = PlcAddressMap.CoilPoints.Values.OrderBy(x => x.DocumentNumber),
        holdingRegisters = PlcAddressMap.HoldingRegisterPoints.Values.OrderBy(x => x.DocumentNumber)
    }));

    app.MapPost("/api/simulator/reset", (VirtualPlcEngine engine) =>
    {
        engine.ResetSimulation();
        return Results.Ok(new
        {
            success = true,
            message = "Virtual PLC reset completed. PC_System_Ready must be set again."
        });
    });

    app.MapPost("/api/simulator/faults/{fault}",
        (string fault, VirtualPlcEngine engine) =>
        {
            if (!Enum.TryParse<SimulationFault>(fault, true, out var parsed))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = $"Unknown fault: {fault}",
                    allowed = Enum.GetNames<SimulationFault>()
                });
            }

            var result = engine.InjectFault(parsed);
            return Results.Ok(new { success = result.Accepted, message = result.Message });
        });

    app.MapGet("/api/simulator/flow-decision",
        (string category, bool stepSucceeded, VirtualPlcEngine engine) =>
        {
            if (!Enum.TryParse<FlowFailureCategory>(category, true, out var parsed))
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = $"Unknown category: {category}",
                    allowed = Enum.GetNames<FlowFailureCategory>()
                });
            }

            return Results.Ok(engine.ResolveFlowDecision(parsed, stepSucceeded));
        });
}
else
{
    app.MapGet("/api/v13/state", (Protocol20260911Engine engine) => Results.Ok(engine.Snapshot()));
    app.MapGet("/api/v13/trace", (Protocol20260911Store store) => Results.Ok(store.Traces));
    app.MapPost("/api/v13/faults/{fault}", (string fault, bool active, Protocol20260911Engine engine) =>
    {
        if (!Enum.TryParse<Protocol20260911Fault>(fault, true, out var parsed))
            return Results.BadRequest(new { error = "Unknown synthetic fault", allowed = Enum.GetNames<Protocol20260911Fault>() });
        engine.SetFault(parsed, active);
        return Results.Ok(engine.Snapshot());
    });
}

app.Lifetime.ApplicationStarted.Register(() =>
{
    var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DashboardOptions>>().Value;
    if (protocolProfile == "LegacyV6") DashboardLauncher.TryOpen(options, app.Logger);
});

app.Run();
