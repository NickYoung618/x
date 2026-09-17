using System.Net;
using Inspection.Application.Plc;

namespace Inspection.Infrastructure.Plc;

public sealed class PlcConnectionOptions
{
    public string Provider { get; set; } = "Disabled";
    public string Contract { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public byte UnitId { get; set; } = 1;
    public int IoTimeoutMs { get; set; } = 1000;
    public int ActionTimeoutMs { get; set; } = 2000;
    public string AddressConvention { get; set; } = "";
    public Float32ByteOrder? Float32ByteOrder { get; set; }
    public string CoordinateFrame { get; set; } = "";
    public string Unit { get; set; } = "";
    public bool AllowVirtualActions { get; set; }
}

/// <summary>Contract selection is explicit; a failed real endpoint never falls back to virtual.</summary>
public static class PlcDeviceFactory
{
    public static IPlcDevice Create(PlcConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Provider == "Disabled") return new DisabledPlcDevice();
        if (options.Provider is not ("Virtual" or "Real"))
            throw new ArgumentException("Plc:Provider must be Disabled, Virtual or Real.");
        if (options.Contract != Protocol20260911Map.Contract)
            throw new ArgumentException("Plc:Contract must select the 2026-09-11 protocol; V6 uses --plc-probe.");
        if (options.AddressConvention != Protocol20260911Map.AddressConvention)
            throw new ArgumentException("Plc:AddressConvention must explicitly select HexOneBased.");
        if (options.Float32ByteOrder is null || !Enum.IsDefined(options.Float32ByteOrder.Value))
            throw new ArgumentException("Plc:Float32ByteOrder must be explicitly calibrated.");
        if (string.IsNullOrWhiteSpace(options.CoordinateFrame) || string.IsNullOrWhiteSpace(options.Unit))
            throw new ArgumentException("Plc:CoordinateFrame and Plc:Unit must be explicit.");
        if (string.IsNullOrWhiteSpace(options.Host)) throw new ArgumentException("Plc:Host is required.");
        if (options.Provider == "Virtual" &&
            (!IPAddress.TryParse(options.Host, out var ip) || !IPAddress.IsLoopback(ip)))
            throw new ArgumentException("Virtual PLC requires a loopback endpoint.");
        if (options.Provider == "Real" && options.AllowVirtualActions)
            throw new ArgumentException("Real PLC actions are blocked pending signed wire and safety verification.");
        if (options.Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(options.Port));
        if (options.UnitId is < 1 or > 247) throw new ArgumentOutOfRangeException(nameof(options.UnitId));
        if (options.IoTimeoutMs is < 50 or > 10000) throw new ArgumentOutOfRangeException(nameof(options.IoTimeoutMs));
        if (options.ActionTimeoutMs is < 100 or > 120000) throw new ArgumentOutOfRangeException(nameof(options.ActionTimeoutMs));
        var transport = new ModbusTcpClient(options.Host, options.Port, options.UnitId,
            TimeSpan.FromMilliseconds(options.IoTimeoutMs));
        return new Protocol20260911PlcDevice(options, transport);
    }

    private sealed class DisabledPlcDevice : IPlcDevice
    {
        public string Source => "Disabled";
        public string Contract => "";
        private static Task<T> Fail<T>() => Task.FromException<T>(new InvalidOperationException("PLC is disabled by configuration."));
        public Task<PlcSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default) => Fail<PlcSnapshot>();
        public Task<PlcResult> SetPcReadyAsync(CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public Task<PlcResult> SetPalletLockAsync(bool locked, CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public Task<PlcResult> ConfigureZonesAsync(ushort ngCapacity, ushort pendingCapacity,
            CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public Task<PlcResult> SubmitMoveAsync(Guid operationId, PlcMoveTarget target,
            CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public Task<PlcResult> WaitForMoveAsync(Guid operationId, CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public Task<PlcResult> SubmitFlipAsync(Guid operationId, ushort targetFace,
            CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public Task<PlcResult> WaitForFlipAsync(Guid operationId, CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public Task<PlcResult> SubmitSortAsync(Guid operationId, PlcSortRequest request,
            CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public Task<PlcResult> RequestSoftStopAsync(CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public Task<PlcResult> ConfirmManualFlipAsync(CancellationToken cancellationToken = default) => Fail<PlcResult>();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
