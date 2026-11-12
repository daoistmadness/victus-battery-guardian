using System;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Windows.Native;

namespace VictusGuardian.Windows.Telemetry;

public class WindowsPowerStatusReader : IWindowsPowerStatusReader
{
    private readonly INativeBatteryDeviceApi _nativeApi;

    public bool IsSupported => _nativeApi.IsSupported;

    public WindowsPowerStatusReader(INativeBatteryDeviceApi nativeApi)
    {
        _nativeApi = nativeApi;
    }

    public Task<BatteryReading> ReadTelemetryAsync(CancellationToken cancellationToken = default)
    {
        if (!_nativeApi.IsSupported)
        {
            return Task.FromResult(CreateUnsupportedReading("Win32 GetSystemPowerStatus unsupported on this OS"));
        }

        if (!_nativeApi.GetSystemPowerStatus(out var status))
        {
            return Task.FromResult(CreateUnsupportedReading("Win32 GetSystemPowerStatus returned false"));
        }

        var powerStatus = status.ACLineStatus switch
        {
            0 => AcPowerStatus.Offline,
            1 => AcPowerStatus.Online,
            _ => AcPowerStatus.Unknown
        };

        var chargingState = BatteryChargingState.Unknown;
        if ((status.BatteryFlag & 8) != 0)
        {
            chargingState = BatteryChargingState.Charging;
        }
        else if (status.ACLineStatus == 1 && status.BatteryLifePercent >= 99)
        {
            chargingState = BatteryChargingState.Full;
        }
        else if (status.ACLineStatus == 0)
        {
            chargingState = (status.BatteryFlag & 4) != 0
                ? BatteryChargingState.Critical
                : BatteryChargingState.Discharging;
        }
        else if (status.ACLineStatus == 1)
        {
            chargingState = BatteryChargingState.NotCharging;
        }

        var percentReading = status.BatteryLifePercent <= 100
            ? TelemetryValue<double>.Reported((double)status.BatteryLifePercent)
            : TelemetryValue<double>.Unavailable;

        var available = SensorCapabilities.PowerStatus | SensorCapabilities.ChargingState;
        if (percentReading.HasValue)
        {
            available |= SensorCapabilities.Percentage;
        }

        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = available,
            BatteryPercentage = percentReading,
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(powerStatus),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(chargingState),
            RemainingCapacityMwh = TelemetryValue<double>.Unavailable,
            FullChargeCapacityMwh = TelemetryValue<double>.Unavailable,
            DesignCapacityMwh = TelemetryValue<double>.Unavailable,
            ChargeRateMilliwatts = TelemetryValue<double>.Unavailable,
            CycleCount = TelemetryValue<int>.Unavailable,
            VoltageMillivolts = TelemetryValue<double>.Unavailable,
            BatteryName = TelemetryValue<string>.Unavailable,
            Manufacturer = TelemetryValue<string>.Unavailable,
            SerialNumber = TelemetryValue<string>.Unavailable,
            Chemistry = TelemetryValue<string>.Unavailable,
            IsStale = false,
            TelemetryProviderSource = "Windows Win32 (GetSystemPowerStatus Fallback)"
        };

        return Task.FromResult(reading);
    }

    private static BatteryReading CreateUnsupportedReading(string reason) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        AvailableSensors = SensorCapabilities.None,
        BatteryPercentage = TelemetryValue<double>.Unavailable,
        PowerStatus = TelemetryValue<AcPowerStatus>.Unavailable,
        ChargingState = TelemetryValue<BatteryChargingState>.Unavailable,
        RemainingCapacityMwh = TelemetryValue<double>.Unavailable,
        FullChargeCapacityMwh = TelemetryValue<double>.Unavailable,
        DesignCapacityMwh = TelemetryValue<double>.Unavailable,
        ChargeRateMilliwatts = TelemetryValue<double>.Unavailable,
        CycleCount = TelemetryValue<int>.Unavailable,
        VoltageMillivolts = TelemetryValue<double>.Unavailable,
        BatteryName = TelemetryValue<string>.Unavailable,
        Manufacturer = TelemetryValue<string>.Unavailable,
        SerialNumber = TelemetryValue<string>.Unavailable,
        Chemistry = TelemetryValue<string>.Unavailable,
        IsStale = false,
        TelemetryProviderSource = reason
    };
}
