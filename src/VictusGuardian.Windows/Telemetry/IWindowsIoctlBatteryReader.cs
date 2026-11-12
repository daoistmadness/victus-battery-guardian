using System;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;

namespace VictusGuardian.Windows.Telemetry;

public interface IWindowsIoctlBatteryReader
{
    bool IsSupported { get; }
    uint? LastKnownBatteryTag { get; }
    string? LastKnownSerialNumber { get; }
    bool WasTagReinitialized { get; }
    bool HasHardwareIdentityChanged { get; }
    bool WasBatteryReplaced => HasHardwareIdentityChanged;
    Task<BatteryReading> ReadTelemetryAsync(CancellationToken cancellationToken = default);
    void InvalidateTagCache();
}
