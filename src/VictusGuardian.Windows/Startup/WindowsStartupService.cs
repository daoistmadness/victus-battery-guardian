using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace VictusGuardian.Windows.Startup;

public class WindowsStartupService : IWindowsStartupService
{
    public const string DefaultValueName = "VictusBatteryGuardian";
    public const string RunRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;
    private readonly string _subKeyPath;

    public WindowsStartupService(string valueName = DefaultValueName, string subKeyPath = RunRegistrySubKey)
    {
        _valueName = valueName;
        _subKeyPath = subKeyPath;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKeyPath, writable: false);
            if (key == null) return false;

            var value = key.GetValue(_valueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public bool Register(bool startMinimized = false, string? customExecutablePath = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            var exePath = customExecutablePath ?? Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                return false;

            using var key = Registry.CurrentUser.OpenSubKey(_subKeyPath, writable: true);
            if (key == null) return false;

            var command = startMinimized ? $"\"{exePath}\" --minimized" : $"\"{exePath}\"";
            key.SetValue(_valueName, command, RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Unregister()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_subKeyPath, writable: true);
            if (key == null) return false;

            key.DeleteValue(_valueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
