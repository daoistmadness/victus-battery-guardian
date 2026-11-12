using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VictusGuardian.Windows.Native;

public class SafeBatteryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly Action<IntPtr>? _onRelease;

    public SafeBatteryHandle() : base(true)
    {
    }

    public SafeBatteryHandle(IntPtr preexistingHandle, bool ownsHandle, Action<IntPtr>? onRelease = null)
        : base(ownsHandle)
    {
        SetHandle(preexistingHandle);
        _onRelease = onRelease;
    }

    protected override bool ReleaseHandle()
    {
        if (_onRelease is not null)
        {
            _onRelease(handle);
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return CloseHandle(handle);
        }

        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
