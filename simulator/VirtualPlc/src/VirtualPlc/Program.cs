using VirtualPlc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ModbusOptions>(builder.Configuration.GetSection("Modbus"));
builder.Services.Configure<SimulationOptions>(builder.Configuration.GetSection("Simulation"));
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));
builder.Services.AddSingleton<PlcDataStore>();
builder.Services.AddSingleton<VirtualPlcEngine>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<VirtualPlcEngine>());
builder.Services.AddHostedService<ModbusTcpServer>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "VirtualPlc",
    timestamp = DateTimeOffset.UtcNow
}));

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

app.Lifetime.ApplicationStarted.Register(() =>
{
    var options = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DashboardOptions>>().Value;
    DashboardLauncher.TryOpen(options, app.Logger);
});

app.Run();
