using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Windows.Native;

namespace VictusGuardian.Windows.Telemetry;

public class WindowsIoctlBatteryReader : IWindowsIoctlBatteryReader
{
    private readonly INativeBatteryDeviceApi _nativeApi;
    private uint? _lastKnownBatteryTag;
    private string? _lastKnownSerialNumber;
    private bool _wasTagReinitialized;
    private bool _hasHardwareIdentityChanged;

    public bool IsSupported => _nativeApi.IsSupported;
    public uint? LastKnownBatteryTag => _lastKnownBatteryTag;
    public string? LastKnownSerialNumber => _lastKnownSerialNumber;
    public bool WasTagReinitialized => _wasTagReinitialized;
    public bool HasHardwareIdentityChanged => _hasHardwareIdentityChanged;
    public bool WasBatteryReplaced => HasHardwareIdentityChanged;

    public WindowsIoctlBatteryReader(INativeBatteryDeviceApi nativeApi)
    {
        _nativeApi = nativeApi;
    }

    public void InvalidateTagCache()
    {
        _lastKnownBatteryTag = null;
        _lastKnownSerialNumber = null;
        _wasTagReinitialized = false;
        _hasHardwareIdentityChanged = false;
    }

    public Task<BatteryReading> ReadTelemetryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_nativeApi.IsSupported)
        {
            return Task.FromResult(CreateUnsupportedReading("Windows IOCTL unsupported on this OS"));
        }

        var devicePaths = _nativeApi.EnumerateBatteryDevicePaths();
        if (devicePaths.Count == 0)
        {
            return Task.FromResult(CreateUnsupportedReading("No battery device interfaces discovered"));
        }

        BatteryReading? fallbackCandidate = null;

        // Iterate through enumerated battery device paths.
        // In systems with multiple batteries, prioritize BATTERY_SYSTEM_BATTERY.
        foreach (var path in devicePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var handle = _nativeApi.OpenBatteryDevice(path);
            if (handle.IsInvalid) continue;

            // Use 2000ms timeout for query tag to prevent indefinite block if ACPI EC hangs
            if (!_nativeApi.QueryBatteryTag(handle, 2000, out var tag) || tag == BatteryNativeConstants.BATTERY_TAG_INVALID)
            {
                continue;
            }

            var infoSuccess = _nativeApi.QueryBatteryInformation(handle, tag, out var info);
            var statusSuccess = _nativeApi.QueryBatteryStatus(handle, tag, out var status);

            if (!statusSuccess && !infoSuccess)
            {
                continue;
            }

            // Distinguish driver tag re-initialization (session reset) from true physical battery change
            if (_lastKnownBatteryTag.HasValue && _lastKnownBatteryTag.Value != tag)
            {
                _wasTagReinitialized = true;
            }
            _lastKnownBatteryTag = tag;

            var serial = _nativeApi.QueryBatteryString(handle, tag, BatteryQueryInformationLevel.BatterySerialNumber);
            if (!string.IsNullOrEmpty(serial))
            {
                if (!string.IsNullOrEmpty(_lastKnownSerialNumber) && !string.Equals(_lastKnownSerialNumber, serial, StringComparison.OrdinalIgnoreCase))
                {
                    _hasHardwareIdentityChanged = true;
                }
                _lastKnownSerialNumber = serial;
            }

            var reading = ParseBatteryReading(path, tag, handle, infoSuccess ? info : null, statusSuccess ? status : null, serial);

            // If this is identified as the system battery, return immediately
            if (infoSuccess && (info.Capabilities & BatteryNativeConstants.BATTERY_SYSTEM_BATTERY) != 0)
            {
                return Task.FromResult(reading);
            }

            fallbackCandidate ??= reading;
        }

        if (fallbackCandidate != null)
        {
            return Task.FromResult(fallbackCandidate);
        }

        return Task.FromResult(CreateUnsupportedReading("Unable to query active battery on any enumerated interface"));
    }

    private BatteryReading ParseBatteryReading(
        string devicePath,
        uint tag,
        SafeBatteryHandle handle,
        BATTERY_INFORMATION? info,
        BATTERY_STATUS? status,
        string? serialNumber)
    {
        var capabilities = SensorCapabilities.None;

        // 1. AC & Power Status
        var acStatus = AcPowerStatus.Unknown;
        var chargingState = BatteryChargingState.Unknown;

        if (status.HasValue)
        {
            capabilities |= SensorCapabilities.PowerStatus | SensorCapabilities.ChargingState;
            var isOnline = (status.Value.PowerState & BatteryNativeConstants.BATTERY_POWER_ON_LINE) != 0;
            acStatus = isOnline ? AcPowerStatus.Online : AcPowerStatus.Offline;

            if ((status.Value.PowerState & BatteryNativeConstants.BATTERY_CHARGING) != 0)
            {
                chargingState = (status.Value.PowerState & BatteryNativeConstants.BATTERY_CRITICAL) != 0
                    ? BatteryChargingState.Critical
                    : BatteryChargingState.Charging;
            }
            else if ((status.Value.PowerState & BatteryNativeConstants.BATTERY_DISCHARGING) != 0)
            {
                chargingState = (status.Value.PowerState & BatteryNativeConstants.BATTERY_CRITICAL) != 0
                    ? BatteryChargingState.Critical
                    : BatteryChargingState.Discharging;
            }
            else if (isOnline)
            {
                // On AC and not charging: distinguish full from charge-limit / idle
                if (status.Value.Capacity != BatteryNativeConstants.BATTERY_UNKNOWN_CAPACITY &&
                    info.HasValue && info.Value.FullChargedCapacity != BatteryNativeConstants.BATTERY_UNKNOWN_CAPACITY &&
                    info.Value.FullChargedCapacity > 0 &&
                    status.Value.Capacity >= (info.Value.FullChargedCapacity * 0.95))
                {
                    chargingState = BatteryChargingState.Full;
                }
                else
                {
                    chargingState = BatteryChargingState.NotCharging;
                }
            }
            else
            {
                chargingState = BatteryChargingState.NotCharging;
            }
        }

        // 2. Capacities
        var isRelative = info.HasValue && (info.Value.Capabilities & BatteryNativeConstants.BATTERY_CAPACITY_RELATIVE) != 0;

        var designCap = TelemetryValue<double>.Unavailable;
        if (info.HasValue && info.Value.DesignedCapacity != BatteryNativeConstants.BATTERY_UNKNOWN_CAPACITY)
        {
            capabilities |= SensorCapabilities.DesignCapacity;
            designCap = TelemetryValue<double>.Reported(info.Value.DesignedCapacity);
        }

        var fullCap = TelemetryValue<double>.Unavailable;
        if (info.HasValue && info.Value.FullChargedCapacity != BatteryNativeConstants.BATTERY_UNKNOWN_CAPACITY)
        {
            capabilities |= SensorCapabilities.FullChargeCapacity;
            fullCap = TelemetryValue<double>.Reported(info.Value.FullChargedCapacity);
        }

        var remainingCap = TelemetryValue<double>.Unavailable;
        if (status.HasValue && status.Value.Capacity != BatteryNativeConstants.BATTERY_UNKNOWN_CAPACITY)
        {
            capabilities |= SensorCapabilities.RemainingCapacity;
            remainingCap = TelemetryValue<double>.Reported(status.Value.Capacity);
        }

        // 3. Percentage
        var percentage = TelemetryValue<double>.Unavailable;
        if (isRelative && remainingCap.HasValue)
        {
            capabilities |= SensorCapabilities.Percentage;
            if (fullCap.HasValue && fullCap.Value > 0)
            {
                var pct = Math.Clamp((remainingCap.Value / fullCap.Value) * 100.0, 0.0, 100.0);
                percentage = TelemetryValue<double>.Reported(Math.Round(pct, 1));
            }
            else
            {
                percentage = TelemetryValue<double>.Reported(Math.Clamp(remainingCap.Value, 0.0, 100.0));
            }
        }
        else if (remainingCap.HasValue && fullCap.HasValue && fullCap.Value > 0)
        {
            capabilities |= SensorCapabilities.Percentage;
            var pct = Math.Clamp((remainingCap.Value / fullCap.Value) * 100.0, 0.0, 100.0);
            percentage = TelemetryValue<double>.Reported(Math.Round(pct, 1));
        }

        // 4. Voltage
        var voltage = TelemetryValue<double>.Unavailable;
        if (status.HasValue && status.Value.Voltage != BatteryNativeConstants.BATTERY_UNKNOWN_VOLTAGE && status.Value.Voltage > 0)
        {
            capabilities |= SensorCapabilities.Voltage;
            voltage = TelemetryValue<double>.Reported(status.Value.Voltage);
        }

        // 5. Rate (signed: positive for charging, negative for discharging)
        var rate = TelemetryValue<double>.Unavailable;
        if (status.HasValue && status.Value.Rate != BatteryNativeConstants.BATTERY_UNKNOWN_RATE)
        {
            capabilities |= SensorCapabilities.ChargeRate;
            double normalizedRate;
            if ((status.Value.PowerState & BatteryNativeConstants.BATTERY_CHARGING) != 0)
            {
                normalizedRate = Math.Abs(status.Value.Rate);
            }
            else if ((status.Value.PowerState & BatteryNativeConstants.BATTERY_DISCHARGING) != 0)
            {
                normalizedRate = -Math.Abs(status.Value.Rate);
            }
            else if (status.Value.Rate != 0)
            {
                normalizedRate = status.Value.Rate;
            }
            else
            {
                normalizedRate = 0.0;
            }
            rate = TelemetryValue<double>.Reported(normalizedRate);
        }

        // 6. Cycle Count
        var cycles = TelemetryValue<int>.Unavailable;
        if (info.HasValue && info.Value.CycleCount > 0)
        {
            capabilities |= SensorCapabilities.CycleCount;
            cycles = TelemetryValue<int>.Reported((int)info.Value.CycleCount);
        }

        // 7. Identity & Metadata
        var chemistry = info.HasValue ? TelemetryValue<string>.Reported(info.Value.GetChemistryString()) : TelemetryValue<string>.Unavailable;
        var devName = _nativeApi.QueryBatteryString(handle, tag, BatteryQueryInformationLevel.BatteryDeviceName);
        var mfgName = _nativeApi.QueryBatteryString(handle, tag, BatteryQueryInformationLevel.BatteryManufactureName);

        if (!string.IsNullOrEmpty(devName) || !string.IsNullOrEmpty(mfgName) || !string.IsNullOrEmpty(serialNumber))
        {
            capabilities |= SensorCapabilities.BatteryIdentity;
        }

        return new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = capabilities,
            BatteryPercentage = percentage,
            PowerStatus = status.HasValue ? TelemetryValue<AcPowerStatus>.Reported(acStatus) : TelemetryValue<AcPowerStatus>.Unavailable,
            ChargingState = status.HasValue ? TelemetryValue<BatteryChargingState>.Reported(chargingState) : TelemetryValue<BatteryChargingState>.Unavailable,
            RemainingCapacityMwh = remainingCap,
            FullChargeCapacityMwh = fullCap,
            DesignCapacityMwh = designCap,
            ChargeRateMilliwatts = rate,
            VoltageMillivolts = voltage,
            CycleCount = cycles,
            BatteryName = !string.IsNullOrEmpty(devName) ? TelemetryValue<string>.Reported(devName) : TelemetryValue<string>.Unavailable,
            Manufacturer = !string.IsNullOrEmpty(mfgName) ? TelemetryValue<string>.Reported(mfgName) : TelemetryValue<string>.Unavailable,
            SerialNumber = !string.IsNullOrEmpty(serialNumber) ? TelemetryValue<string>.Reported(serialNumber) : TelemetryValue<string>.Unavailable,
            Chemistry = chemistry,
            BatteryTag = tag,
            DevicePath = devicePath,
            IsStale = false,
            TelemetryProviderSource = "Windows IOCTL (Direct Device Interface)"
        };
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
        TelemetryProviderSource = $"Telemetry Unavailable: {reason}"
    };
}
