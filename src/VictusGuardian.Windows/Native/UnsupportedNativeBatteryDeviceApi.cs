using System;
using System.Collections.Generic;

namespace VictusGuardian.Windows.Native;

public class UnsupportedNativeBatteryDeviceApi : INativeBatteryDeviceApi
{
    public bool IsSupported => false;

    public bool GetSystemPowerStatus(out NativeSystemPowerStatus status)
    {
        status = default;
        return false;
    }

    public IReadOnlyList<string> EnumerateBatteryDevicePaths() => Array.Empty<string>();

    public SafeBatteryHandle OpenBatteryDevice(string devicePath) => new(IntPtr.Zero, false);

    public bool QueryBatteryTag(SafeBatteryHandle handle, uint timeoutMs, out uint batteryTag)
    {
        batteryTag = BatteryNativeConstants.BATTERY_TAG_INVALID;
        return false;
    }

    public bool QueryBatteryInformation(SafeBatteryHandle handle, uint batteryTag, out BATTERY_INFORMATION info)
    {
        info = default;
        return false;
    }

    public bool QueryBatteryStatus(SafeBatteryHandle handle, uint batteryTag, out BATTERY_STATUS status)
    {
        status = default;
        return false;
    }

    public string? QueryBatteryString(SafeBatteryHandle handle, uint batteryTag, BatteryQueryInformationLevel level) => null;
}
