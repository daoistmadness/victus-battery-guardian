using System;
using System.Collections.Generic;
using VictusGuardian.Windows.Native;

namespace VictusGuardian.UnitTests;

public class MockNativeBatteryDeviceApi : INativeBatteryDeviceApi
{
    public bool IsSupported { get; set; } = true;
    public List<string> DevicePaths { get; } = new();
    public int OpenDeviceCallCount { get; private set; }
    public int ReleaseHandleCallCount { get; private set; }

    public bool ShouldFailOpen { get; set; }
    public bool ShouldFailQueryTag { get; set; }
    public bool ShouldFailQueryInfo { get; set; }
    public bool ShouldFailQueryStatus { get; set; }
    public bool ShouldFailPowerStatus { get; set; }

    public uint NextBatteryTag { get; set; } = 1;
    public BATTERY_INFORMATION SimulatedInfo { get; set; }
    public BATTERY_STATUS SimulatedStatus { get; set; }
    public NativeSystemPowerStatus SimulatedPowerStatus { get; set; }

    public string? SimulatedDeviceName { get; set; } = "Primary";
    public string? SimulatedManufactureName { get; set; } = "HP / Celxpert";
    public string? SimulatedSerialNumber { get; set; } = "SN-2024-TEST";

    public MockNativeBatteryDeviceApi()
    {
        DevicePaths.Add(@"\\?\acpi#pnp0c0a#1#{72631e54-78a4-11d0-bcf7-00aa00b7b32a}");

        // HP Victus 70 Wh default
        SimulatedInfo = new BATTERY_INFORMATION
        {
            Capabilities = BatteryNativeConstants.BATTERY_SYSTEM_BATTERY,
            Technology = 1,
            Chemistry0 = (byte)'L',
            Chemistry1 = (byte)'I',
            Chemistry2 = (byte)'O',
            Chemistry3 = (byte)'N',
            DesignedCapacity = 70070,
            FullChargedCapacity = 66500,
            DefaultAlert1 = 5000,
            DefaultAlert2 = 2500,
            CriticalBias = 0,
            CycleCount = 42
        };

        SimulatedStatus = new BATTERY_STATUS
        {
            PowerState = BatteryNativeConstants.BATTERY_DISCHARGING,
            Capacity = 52202,
            Voltage = 15400,
            Rate = 18500
        };

        SimulatedPowerStatus = new NativeSystemPowerStatus
        {
            ACLineStatus = 0,
            BatteryFlag = 0,
            BatteryLifePercent = 78,
            SystemStatusFlag = 0,
            BatteryLifeTime = 10000,
            BatteryFullLifeTime = -1
        };
    }

    public bool GetSystemPowerStatus(out NativeSystemPowerStatus status)
    {
        if (!IsSupported || ShouldFailPowerStatus)
        {
            status = default;
            return false;
        }

        status = SimulatedPowerStatus;
        return true;
    }

    public IReadOnlyList<string> EnumerateBatteryDevicePaths()
    {
        if (!IsSupported) return Array.Empty<string>();
        return DevicePaths.ToArray();
    }

    public SafeBatteryHandle OpenBatteryDevice(string devicePath)
    {
        OpenDeviceCallCount++;
        if (!IsSupported || ShouldFailOpen)
        {
            return new SafeBatteryHandle(IntPtr.Zero, false);
        }

        // Return a mock handle with trackable release callback
        return new SafeBatteryHandle(new IntPtr(12345), true, _ => ReleaseHandleCallCount++);
    }

    public bool QueryBatteryTag(SafeBatteryHandle handle, uint timeoutMs, out uint batteryTag)
    {
        if (!IsSupported || ShouldFailQueryTag || handle.IsInvalid)
        {
            batteryTag = BatteryNativeConstants.BATTERY_TAG_INVALID;
            return false;
        }

        batteryTag = NextBatteryTag;
        return true;
    }

    public bool QueryBatteryInformation(SafeBatteryHandle handle, uint batteryTag, out BATTERY_INFORMATION info)
    {
        if (!IsSupported || ShouldFailQueryInfo || handle.IsInvalid || batteryTag == BatteryNativeConstants.BATTERY_TAG_INVALID)
        {
            info = default;
            return false;
        }

        info = SimulatedInfo;
        return true;
    }

    public bool QueryBatteryStatus(SafeBatteryHandle handle, uint batteryTag, out BATTERY_STATUS status)
    {
        if (!IsSupported || ShouldFailQueryStatus || handle.IsInvalid || batteryTag == BatteryNativeConstants.BATTERY_TAG_INVALID)
        {
            status = default;
            return false;
        }

        status = SimulatedStatus;
        return true;
    }

    public string? QueryBatteryString(SafeBatteryHandle handle, uint batteryTag, BatteryQueryInformationLevel level)
    {
        if (!IsSupported || handle.IsInvalid || batteryTag == BatteryNativeConstants.BATTERY_TAG_INVALID)
        {
            return null;
        }

        return level switch
        {
            BatteryQueryInformationLevel.BatteryDeviceName => SimulatedDeviceName,
            BatteryQueryInformationLevel.BatteryManufactureName => SimulatedManufactureName,
            BatteryQueryInformationLevel.BatterySerialNumber => SimulatedSerialNumber,
            _ => null
        };
    }
}
