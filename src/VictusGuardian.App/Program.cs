using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using VictusGuardian.App.Diagnostics;
using VictusGuardian.App.Lifecycle;

namespace VictusGuardian.App;

internal sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 1. Independent Diagnostics Execution (bypasses single-instance GUI guard)
        if (args.Any(arg => arg.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("-d", StringComparison.OrdinalIgnoreCase)))
        {
            return DiagnosticRunner.RunAsync(args).GetAwaiter().GetResult();
        }

        // 2. Initialize Startup Diagnostics Logger
        AppLogger.Initialize();
        AppLogger.LogInfo($"Application process started. PID: {Environment.ProcessId}, Version: {AppVersion.FullVersion}, Commit: {AppVersion.CommitSha}");

        // 3. Single-Instance Verification (Named Mutex scoped to current user session)
        using var singleInstance = new SingleInstanceGuard();
        if (!singleInstance.TryAcquire())
        {
            AppLogger.LogInfo("An existing GUI instance is already running. Signaling existing instance to activate and exiting immediately.");
            singleInstance.SignalExistingInstance();
            return 0; // Clean exit, preventing background process accumulation
        }

        // 4. Start Local IPC Activation Server to restore window on repeated user launches
        singleInstance.StartActivationServer(() =>
        {
            AppLogger.LogInfo("Received activation signal from secondary launch attempt.");
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                        desktop.MainWindow is { } window)
                    {
                        if (window.WindowState == WindowState.Minimized)
                        {
                            window.WindowState = WindowState.Normal;
                        }
                        window.Show();
                        window.Activate();
                        window.Focus();
                        window.Topmost = true;
                        window.Topmost = false;
                        AppLogger.LogInfo("Existing MainWindow successfully restored and brought to foreground.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"Error activating existing window from IPC signal: {ex.Message}");
                }
            });
        });

        // 5. Start Avalonia Desktop Application
        try
        {
            AppLogger.LogInfo("Starting Avalonia desktop application lifetime...");
            var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            AppLogger.LogInfo($"Avalonia application lifetime terminated normally with exit code {exitCode}.");
            return exitCode;
        }
        catch (Exception ex)
        {
            AppLogger.LogFatal($"Fatal unhandled exception in application lifetime: {ex}");
            return 1;
        }
        finally
        {
            AppLogger.LogInfo("Process shutdown sequence finished.");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
