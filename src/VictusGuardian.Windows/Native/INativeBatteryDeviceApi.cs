using System;
using System.Collections.Generic;

namespace VictusGuardian.Windows.Native;

public interface INativeBatteryDeviceApi
{
    bool IsSupported { get; }

    bool GetSystemPowerStatus(out NativeSystemPowerStatus status);

    IReadOnlyList<string> EnumerateBatteryDevicePaths();

    SafeBatteryHandle OpenBatteryDevice(string devicePath);

    bool QueryBatteryTag(SafeBatteryHandle handle, uint timeoutMs, out uint batteryTag);

    bool QueryBatteryInformation(SafeBatteryHandle handle, uint batteryTag, out BATTERY_INFORMATION info);

    bool QueryBatteryStatus(SafeBatteryHandle handle, uint batteryTag, out BATTERY_STATUS status);

    string? QueryBatteryString(SafeBatteryHandle handle, uint batteryTag, BatteryQueryInformationLevel level);
}
