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

public class Program
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
            var exitCode = BuildAvaloniaApp(args).StartWithClassicDesktopLifetime(args);
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

    public enum RenderMode
    {
        Software,
        Hardware
    }

    public static bool IsHardwareRenderingRequested(string[]? args = null)
    {
        args ??= Environment.GetCommandLineArgs();

        if (args.Any(arg => arg.Equals("--hardware-rendering", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("--hardware", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("--render-mode=hardware", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--render-mode", StringComparison.OrdinalIgnoreCase) &&
                args[i + 1].Equals("hardware", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var envRenderMode = Environment.GetEnvironmentVariable("VICTUS_RENDER_MODE");
        if (string.Equals(envRenderMode, "hardware", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var envHardware = Environment.GetEnvironmentVariable("VICTUS_HARDWARE_RENDERING");
        return envHardware == "1" || string.Equals(envHardware, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSoftwareRenderingRequested(string[]? args = null)
    {
        args ??= Environment.GetCommandLineArgs();

        if (args.Any(arg => arg.Equals("--software-rendering", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("--software", StringComparison.OrdinalIgnoreCase) ||
                            arg.Equals("--render-mode=software", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--render-mode", StringComparison.OrdinalIgnoreCase) &&
                args[i + 1].Equals("software", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var envRenderMode = Environment.GetEnvironmentVariable("VICTUS_RENDER_MODE");
        if (string.Equals(envRenderMode, "software", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var envSoftware = Environment.GetEnvironmentVariable("VICTUS_SOFTWARE_RENDERING");
        return envSoftware == "1" || string.Equals(envSoftware, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static RenderMode GetEffectiveRenderMode(string[]? args = null)
    {
        if (IsHardwareRenderingRequested(args))
        {
            return RenderMode.Hardware;
        }

        // On Windows, Software Rendering is the default. Physical testing on HP Victus demonstrated
        // working set reduction from ~231 MB to 55.9–75.7 MB with negligible CPU utilization (0.3–0.4%).
        return RenderMode.Software;
    }

    public static AppBuilder BuildAvaloniaApp()
        => BuildAvaloniaApp(Environment.GetCommandLineArgs());

    public static AppBuilder BuildAvaloniaApp(string[] args)
    {
        var renderMode = GetEffectiveRenderMode(args);
        var isHardware = renderMode == RenderMode.Hardware;
        AppLogger.LogInfo(isHardware
            ? "Rendering configuration: Hardware Accelerated (Win32RenderingMode.AngleEgl with Software fallback)."
            : "Rendering configuration: Default Software Rendering (Win32RenderingMode.Software, Win32CompositionMode.RedirectionSurface).");

        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        if (OperatingSystem.IsWindows())
        {
            if (isHardware)
            {
                builder.With(new Win32PlatformOptions
                {
                    RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software],
                    CompositionMode = [Win32CompositionMode.WinUIComposition, Win32CompositionMode.DirectComposition, Win32CompositionMode.RedirectionSurface]
                });
            }
            else
            {
                builder.With(new Win32PlatformOptions
                {
                    RenderingMode = [Win32RenderingMode.Software],
                    CompositionMode = [Win32CompositionMode.RedirectionSurface]
                });
            }
        }

        return builder;
    }
}
