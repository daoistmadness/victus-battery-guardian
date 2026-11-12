using System;
using System.Runtime.InteropServices;

namespace VictusGuardian.Windows.Native;

public static class BatteryNativeConstants
{
    // GUID_DEVICE_BATTERY: {72631E54-78A4-11D0-BCF7-00AA00B7B32A}
    public static readonly Guid GUID_DEVICE_BATTERY = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");

    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    // IOCTL Control Codes (FILE_DEVICE_BATTERY = 0x29, METHOD_BUFFERED = 0, FILE_READ_ACCESS = 1)
    public const uint IOCTL_BATTERY_QUERY_TAG = 0x294040;
    public const uint IOCTL_BATTERY_QUERY_INFORMATION = 0x294044;
    public const uint IOCTL_BATTERY_QUERY_STATUS = 0x29404C;

    // Battery Tag Sentinels
    public const uint BATTERY_TAG_INVALID = 0;

    // Battery Status Constants
    public const uint BATTERY_UNKNOWN_CAPACITY = 0xFFFFFFFF;
    public const uint BATTERY_UNKNOWN_VOLTAGE = 0xFFFFFFFF;
    public const int BATTERY_UNKNOWN_RATE = unchecked((int)0x80000000);

    // PowerState flags from IOCTL_BATTERY_QUERY_STATUS
    public const uint BATTERY_POWER_ON_LINE = 0x00000001;
    public const uint BATTERY_DISCHARGING = 0x00000002;
    public const uint BATTERY_CHARGING = 0x00000004;
    public const uint BATTERY_CRITICAL = 0x00000008;

    // Capabilities flags from BATTERY_INFORMATION
    public const uint BATTERY_SYSTEM_BATTERY = 0x80000000;
    public const uint BATTERY_CAPACITY_RELATIVE = 0x40000000;
    public const uint BATTERY_IS_SHORT_TERM = 0x20000000;
    public const uint BATTERY_SET_CHARGE_SUPPORTED = 0x00000001;
    public const uint BATTERY_SET_DISCHARGE_SUPPORTED = 0x00000002;
    public const uint BATTERY_SET_CHARGING_SOURCE_SUPPORTED = 0x00000004;
}

public enum BatteryQueryInformationLevel
{
    BatteryInformation = 0,
    BatteryGranularityInformation = 1,
    BatteryTemperature = 2,
    BatteryManufactureDate = 3,
    BatteryManufactureName = 4,
    BatteryDeviceName = 5,
    BatterySerialNumber = 6,
    BatteryUniqueID = 7,
    BatteryEstimatedTime = 8
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeSystemPowerStatus
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential)]
public struct SP_DEVICE_INTERFACE_DATA
{
    public uint cbSize;
    public Guid InterfaceClassGuid;
    public uint Flags;
    public nuint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
public struct BATTERY_QUERY_INFORMATION
{
    public uint BatteryTag;
    public BatteryQueryInformationLevel InformationLevel;
    public uint AtRate;
}

[StructLayout(LayoutKind.Sequential)]
public struct BATTERY_INFORMATION
{
    public uint Capabilities;
    public byte Technology;
    public byte Reserved0;
    public byte Reserved1;
    public byte Reserved2;
    public byte Chemistry0;
    public byte Chemistry1;
    public byte Chemistry2;
    public byte Chemistry3;
    public uint DesignedCapacity;
    public uint FullChargedCapacity;
    public uint DefaultAlert1;
    public uint DefaultAlert2;
    public uint CriticalBias;
    public uint CycleCount;

    public string GetChemistryString()
    {
        ReadOnlySpan<byte> bytes = [Chemistry0, Chemistry1, Chemistry2, Chemistry3];
        var len = 0;
        while (len < 4 && bytes[len] != 0) len++;
        return len > 0 ? System.Text.Encoding.ASCII.GetString(bytes[..len]).Trim() : "Unknown";
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct BATTERY_WAIT_STATUS
{
    public uint BatteryTag;
    public uint Timeout;
    public uint PowerState;
    public uint LowCapacity;
    public uint HighCapacity;
}

[StructLayout(LayoutKind.Sequential)]
public struct BATTERY_STATUS
{
    public uint PowerState;
    public uint Capacity;
    public uint Voltage;
    public int Rate;
}
