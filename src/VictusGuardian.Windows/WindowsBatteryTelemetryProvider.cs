using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Models;

namespace VictusGuardian.Windows.Telemetry;

/// <summary>
/// Windows platform battery telemetry provider.
/// Implements standard Win32 power status integration with platform guards.
/// Real deep telemetry (IOCTL_BATTERY_QUERY_INFORMATION / WMI) is scheduled for Milestone 2.
/// </summary>
public class WindowsBatteryTelemetryProvider : IBatteryTelemetryProvider
{
    public string ProviderName => "Windows Win32 Power Telemetry";
    public bool IsHardwareSupported => OperatingSystem.IsWindows();

    public Task<BatteryReading> GetReadingAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(CreateUnsupportedReading());
        }

        return Task.FromResult(ReadWindowsTelemetry());
    }

    [SupportedOSPlatform("windows")]
    private static BatteryReading ReadWindowsTelemetry()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status))
        {
            return CreateUnsupportedReading();
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

        var percentReading = status.BatteryLifePercent <= 100
            ? TelemetryValue<double>.Reported((double)status.BatteryLifePercent)
            : TelemetryValue<double>.Unavailable;

        var available = SensorCapabilities.PowerStatus | SensorCapabilities.ChargingState;
        if (percentReading.HasValue)
        {
            available |= SensorCapabilities.Percentage;
        }

        return new BatteryReading
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
            CycleCount = TelemetryValue<int>.Unavailable
        };
    }

    private static BatteryReading CreateUnsupportedReading() => new()
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
        CycleCount = TelemetryValue<int>.Unavailable
    };

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);
    }
}
