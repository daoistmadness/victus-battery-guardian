using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VictusGuardian.Windows.Native;

[SupportedOSPlatform("windows")]
public class WindowsNativeBatteryDeviceApi : INativeBatteryDeviceApi
{
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool GetSystemPowerStatus(out NativeSystemPowerStatus status)
    {
        if (!OperatingSystem.IsWindows())
        {
            status = default;
            return false;
        }

        return NativeMethods.GetSystemPowerStatus(out status);
    }

    public IReadOnlyList<string> EnumerateBatteryDevicePaths()
    {
        var devicePaths = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            return devicePaths;
        }

        var guid = BatteryNativeConstants.GUID_DEVICE_BATTERY;
        var hdev = NativeMethods.SetupDiGetClassDevs(
            ref guid,
            IntPtr.Zero,
            IntPtr.Zero,
            BatteryNativeConstants.DIGCF_PRESENT | BatteryNativeConstants.DIGCF_DEVICEINTERFACE);

        if (hdev == INVALID_HANDLE_VALUE || hdev == IntPtr.Zero)
        {
            return devicePaths;
        }

        try
        {
            var did = new SP_DEVICE_INTERFACE_DATA();
            did.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

            uint index = 0;
            while (NativeMethods.SetupDiEnumDeviceInterfaces(hdev, IntPtr.Zero, ref guid, index, ref did))
            {
                index++;

                // Query required size
                NativeMethods.SetupDiGetDeviceInterfaceDetail(hdev, ref did, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                if (requiredSize == 0) continue;

                var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    // On 64-bit Windows cbSize must be 8; on 32-bit Windows cbSize is (4 + sizeof(WCHAR)) = 6
                    var cbSize = IntPtr.Size == 8 ? 8 : (4 + Marshal.SystemDefaultCharSize);
                    Marshal.WriteInt32(detailBuffer, cbSize);

                    if (NativeMethods.SetupDiGetDeviceInterfaceDetail(hdev, ref did, detailBuffer, requiredSize, out _, IntPtr.Zero))
                    {
                        // DevicePath begins at offset 4
                        var path = Marshal.PtrToStringUni(detailBuffer + 4);
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            devicePaths.Add(path);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(hdev);
        }

        return devicePaths;
    }

    public SafeBatteryHandle OpenBatteryDevice(string devicePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SafeBatteryHandle(IntPtr.Zero, false);
        }

        // Try standard read/write first
        var handle = NativeMethods.CreateFile(
            devicePath,
            BatteryNativeConstants.GENERIC_READ | BatteryNativeConstants.GENERIC_WRITE,
            BatteryNativeConstants.FILE_SHARE_READ | BatteryNativeConstants.FILE_SHARE_WRITE,
            IntPtr.Zero,
            BatteryNativeConstants.OPEN_EXISTING,
            BatteryNativeConstants.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (!handle.IsInvalid)
        {
            return handle;
        }

        // Unprivileged fallback: try GENERIC_READ only
        handle = NativeMethods.CreateFile(
            devicePath,
            BatteryNativeConstants.GENERIC_READ,
            BatteryNativeConstants.FILE_SHARE_READ | BatteryNativeConstants.FILE_SHARE_WRITE,
            IntPtr.Zero,
            BatteryNativeConstants.OPEN_EXISTING,
            BatteryNativeConstants.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        return handle;
    }

    public bool QueryBatteryTag(SafeBatteryHandle handle, uint timeoutMs, out uint batteryTag)
    {
        batteryTag = BatteryNativeConstants.BATTERY_TAG_INVALID;
        if (!OperatingSystem.IsWindows() || handle.IsInvalid)
        {
            return false;
        }

        var inBuffer = timeoutMs;
        var inSize = (uint)sizeof(uint);
        var outSize = (uint)sizeof(uint);

        var inPtr = Marshal.AllocHGlobal((int)inSize);
        var outPtr = Marshal.AllocHGlobal((int)outSize);

        try
        {
            Marshal.WriteInt32(inPtr, unchecked((int)inBuffer));
            if (NativeMethods.DeviceIoControl(handle, BatteryNativeConstants.IOCTL_BATTERY_QUERY_TAG, inPtr, inSize, outPtr, outSize, out var bytesReturned, IntPtr.Zero) &&
                bytesReturned >= sizeof(uint))
            {
                batteryTag = unchecked((uint)Marshal.ReadInt32(outPtr));
                return batteryTag != BatteryNativeConstants.BATTERY_TAG_INVALID;
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(outPtr);
        }
    }

    public bool QueryBatteryInformation(SafeBatteryHandle handle, uint batteryTag, out BATTERY_INFORMATION info)
    {
        info = default;
        if (!OperatingSystem.IsWindows() || handle.IsInvalid || batteryTag == BatteryNativeConstants.BATTERY_TAG_INVALID)
        {
            return false;
        }

        var query = new BATTERY_QUERY_INFORMATION
        {
            BatteryTag = batteryTag,
            InformationLevel = BatteryQueryInformationLevel.BatteryInformation,
            AtRate = 0
        };

        var querySize = (uint)Marshal.SizeOf<BATTERY_QUERY_INFORMATION>();
        var outSize = (uint)Marshal.SizeOf<BATTERY_INFORMATION>();

        var inPtr = Marshal.AllocHGlobal((int)querySize);
        var outPtr = Marshal.AllocHGlobal((int)outSize);

        try
        {
            Marshal.StructureToPtr(query, inPtr, false);
            if (NativeMethods.DeviceIoControl(handle, BatteryNativeConstants.IOCTL_BATTERY_QUERY_INFORMATION, inPtr, querySize, outPtr, outSize, out var bytesReturned, IntPtr.Zero) &&
                bytesReturned >= outSize)
            {
                info = Marshal.PtrToStructure<BATTERY_INFORMATION>(outPtr);
                return true;
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(outPtr);
        }
    }

    public bool QueryBatteryStatus(SafeBatteryHandle handle, uint batteryTag, out BATTERY_STATUS status)
    {
        status = default;
        if (!OperatingSystem.IsWindows() || handle.IsInvalid || batteryTag == BatteryNativeConstants.BATTERY_TAG_INVALID)
        {
            return false;
        }

        var waitStatus = new BATTERY_WAIT_STATUS
        {
            BatteryTag = batteryTag,
            Timeout = 0,
            PowerState = 0,
            LowCapacity = 0,
            HighCapacity = 0
        };

        var inSize = (uint)Marshal.SizeOf<BATTERY_WAIT_STATUS>();
        var outSize = (uint)Marshal.SizeOf<BATTERY_STATUS>();

        var inPtr = Marshal.AllocHGlobal((int)inSize);
        var outPtr = Marshal.AllocHGlobal((int)outSize);

        try
        {
            Marshal.StructureToPtr(waitStatus, inPtr, false);
            if (NativeMethods.DeviceIoControl(handle, BatteryNativeConstants.IOCTL_BATTERY_QUERY_STATUS, inPtr, inSize, outPtr, outSize, out var bytesReturned, IntPtr.Zero) &&
                bytesReturned >= outSize)
            {
                status = Marshal.PtrToStructure<BATTERY_STATUS>(outPtr);
                return true;
            }

            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(outPtr);
        }
    }

    public string? QueryBatteryString(SafeBatteryHandle handle, uint batteryTag, BatteryQueryInformationLevel level)
    {
        if (!OperatingSystem.IsWindows() || handle.IsInvalid || batteryTag == BatteryNativeConstants.BATTERY_TAG_INVALID)
        {
            return null;
        }

        var query = new BATTERY_QUERY_INFORMATION
        {
            BatteryTag = batteryTag,
            InformationLevel = level,
            AtRate = 0
        };

        var querySize = (uint)Marshal.SizeOf<BATTERY_QUERY_INFORMATION>();
        const uint outSize = 512; // 256 WCHARs

        var inPtr = Marshal.AllocHGlobal((int)querySize);
        var outPtr = Marshal.AllocHGlobal((int)outSize);

        try
        {
            Marshal.StructureToPtr(query, inPtr, false);
            if (NativeMethods.DeviceIoControl(handle, BatteryNativeConstants.IOCTL_BATTERY_QUERY_INFORMATION, inPtr, querySize, outPtr, outSize, out var bytesReturned, IntPtr.Zero) &&
                bytesReturned > 0)
            {
                var chars = (int)(bytesReturned / 2);
                return Marshal.PtrToStringUni(outPtr, chars)?.TrimEnd('\0', ' ');
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(outPtr);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus(out NativeSystemPowerStatus lpSystemPowerStatus);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            IntPtr Enumerator,
            IntPtr hwndParent,
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet,
            IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid,
            uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            uint DeviceInterfaceDetailDataSize,
            out uint RequiredSize,
            IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeBatteryHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeBatteryHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);
    }
}
