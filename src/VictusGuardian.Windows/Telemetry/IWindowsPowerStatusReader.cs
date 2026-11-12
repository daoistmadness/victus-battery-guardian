using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;

namespace VictusGuardian.Windows.Telemetry;

public interface IWindowsPowerStatusReader
{
    bool IsSupported { get; }
    Task<BatteryReading> ReadTelemetryAsync(CancellationToken cancellationToken = default);
}
