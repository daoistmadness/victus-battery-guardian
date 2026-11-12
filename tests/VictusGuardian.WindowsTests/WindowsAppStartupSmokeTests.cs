using System;
using System.IO;
using System.Threading.Tasks;
using VictusGuardian.App.Diagnostics;
using VictusGuardian.App.Lifecycle;
using Xunit;

namespace VictusGuardian.WindowsTests;

public class WindowsAppStartupSmokeTests
{
    [Fact]
    public void WindowsApp_SingleInstanceGuard_CreatesValidWindowsNamedMutex()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Windows-specific test
            return;
        }

        var uniqueId = $"SmokeTest_{Guid.NewGuid():N}";
        using var guard = new SingleInstanceGuard(uniqueId);

        Assert.StartsWith("Local\\", guard.MutexName);
        Assert.True(guard.TryAcquire());
        Assert.True(guard.HasAcquired);
    }

    [Fact]
    public void WindowsApp_StartupLogger_CreatesLogsInLocalAppDataDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AppLogger.Initialize();
        AppLogger.LogInfo("Windows smoke test log entry.");

        var logPath = AppLogger.GetLogFilePath();
        Assert.True(File.Exists(logPath));
        Assert.Contains("AppData", logPath, StringComparison.OrdinalIgnoreCase);
    }
}
