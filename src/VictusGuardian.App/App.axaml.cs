using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using VictusGuardian.App.Diagnostics;
using VictusGuardian.App.ViewModels;
using VictusGuardian.App.Views;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Configuration;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Simulation;
using VictusGuardian.Storage.Repositories;
using VictusGuardian.Windows;

namespace VictusGuardian.App;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;
    private CancellationTokenSource? _appCts;

    public override void Initialize()
    {
        AppLogger.LogInfo("Loading Avalonia XAML components...");
        AvaloniaXamlLoader.Load(this);
        AppLogger.LogInfo("Avalonia XAML loaded successfully.");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppLogger.LogInfo("Executing OnFrameworkInitializationCompleted (synchronous UI presentation phase)...");

        try
        {
            var services = new ServiceCollection();

            // 1. Core Services & Configuration
            services.AddSingleton<ISettingsService, JsonFileSettingsService>();
            services.AddSingleton<IBatteryHealthCalculator, DefaultBatteryHealthCalculator>();
            services.AddSingleton<IBatteryAlertEvaluator, DefaultBatteryAlertEvaluator>();
            services.AddSingleton<SimulatedBatteryTelemetryProvider>();
            services.AddSingleton<ISimulatedBatteryService>(sp => sp.GetRequiredService<SimulatedBatteryTelemetryProvider>());

            // 2. Storage Services
            services.AddSingleton<IBatteryHistoryRepository, SqliteBatteryHistoryRepository>();

            // 3. Windows Services & Composite Provider (preferred IOCTL -> fallback Win32 -> unavailable)
            services.AddWindowsGuardianServices();
            services.AddSingleton<IBatteryTelemetryProvider>(sp => sp.GetRequiredService<ICompositeBatteryTelemetryProvider>());

            // 4. ViewModels
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<HealthViewModel>();
            services.AddSingleton<ChargingViewModel>();
            services.AddSingleton<HistoryViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<MainWindowViewModel>();

            Services = services.BuildServiceProvider();
            AppLogger.LogInfo("Dependency injection container built successfully.");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _appCts = new CancellationTokenSource();

                AppLogger.LogInfo("Resolving MainWindowViewModel...");
                var mainWindowVm = Services.GetRequiredService<MainWindowViewModel>();

                AppLogger.LogInfo("Instantiating MainWindow...");
                var mainWindow = new MainWindow
                {
                    DataContext = mainWindowVm
                };

                mainWindow.Opened += (s, e) =>
                {
                    AppLogger.LogInfo("MainWindow Opened event triggered. GUI is visible and interactive on screen.");
                };

                desktop.MainWindow = mainWindow;
                AppLogger.LogInfo("MainWindow assigned to ApplicationLifetime.MainWindow.");

                // Explicitly call Show() to guarantee window display across all desktop life cycles
                mainWindow.Show();
                AppLogger.LogInfo("MainWindow.Show() called.");

                desktop.ShutdownRequested += (s, e) =>
                {
                    AppLogger.LogInfo("Application lifetime ShutdownRequested received.");
                    _appCts?.Cancel();
                };

                desktop.Exit += (s, e) =>
                {
                    AppLogger.LogInfo("Application lifetime Exit event fired. Performing final cleanup.");
                    _appCts?.Cancel();
                };

                // Asynchronously initialize optional services without blocking or delaying window presentation
                _ = InitializeApplicationServicesAsync(mainWindowVm, _appCts.Token);
            }

            base.OnFrameworkInitializationCompleted();
            AppLogger.LogInfo("OnFrameworkInitializationCompleted completed synchronously.");
        }
        catch (Exception ex)
        {
            AppLogger.LogFatal($"Fatal exception during application startup: {ex}");
            throw;
        }
    }

    private async Task InitializeApplicationServicesAsync(MainWindowViewModel mainWindowVm, CancellationToken cancellationToken)
    {
        AppLogger.LogInfo("Beginning background services initialization...");

        // 1. Settings initialization (non-blocking)
        try
        {
            AppLogger.LogInfo("Loading persistent settings from disk...");
            var settingsService = Services.GetRequiredService<ISettingsService>();
            var loadedSettings = await settingsService.LoadSettingsAsync(cancellationToken);
            var composite = Services.GetRequiredService<ICompositeBatteryTelemetryProvider>();
            composite.IsDemoModeEnabled = loadedSettings.EnableDemoMode;
            AppLogger.LogInfo($"Settings loaded successfully (DemoMode={loadedSettings.EnableDemoMode}, Interval={loadedSettings.PollingIntervalSeconds}s).");
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Settings loading failed (retaining defaults): {ex.Message}");
        }

        if (cancellationToken.IsCancellationRequested) return;

        // 2. Storage / SQLite schema initialization (non-blocking)
        try
        {
            AppLogger.LogInfo("Initializing SQLite battery history database...");
            var storage = Services.GetRequiredService<IBatteryHistoryRepository>();
            await storage.InitializeAsync(cancellationToken);
            AppLogger.LogInfo("SQLite battery history database initialized successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"SQLite initialization failed: {ex.Message}");
            await SafeInvokeOnUIThreadAsync(() =>
            {
                mainWindowVm.Dashboard.ActiveAlerts.Add($"⚠ Local database unavailable: {ex.Message}. Telemetry continues in memory.");
                return Task.CompletedTask;
            });
        }

        if (cancellationToken.IsCancellationRequested) return;

        // 3. Initial telemetry refresh (non-blocking)
        try
        {
            AppLogger.LogInfo("Executing initial battery telemetry query...");
            await SafeInvokeOnUIThreadAsync(async () =>
            {
                await mainWindowVm.Dashboard.RefreshTelemetryAsync();
            });
            AppLogger.LogInfo("Initial battery telemetry query completed.");
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Initial telemetry query failed: {ex.Message}");
        }

        if (cancellationToken.IsCancellationRequested) return;

        // 4. Start background telemetry polling timer
        try
        {
            var settingsService = Services.GetRequiredService<ISettingsService>();
            int interval = settingsService.CurrentSettings.PollingIntervalSeconds;
            AppLogger.LogInfo($"Starting background telemetry polling timer ({interval}s interval)...");
            mainWindowVm.Dashboard.StartAutoPolling(interval);
            AppLogger.LogInfo("Background telemetry polling timer started.");
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"Failed to start background polling timer: {ex.Message}");
        }
    }

    private static async Task SafeInvokeOnUIThreadAsync(Func<Task> action)
    {
        try
        {
            if (Dispatcher.UIThread != null && !Dispatcher.UIThread.CheckAccess())
            {
                await Dispatcher.UIThread.InvokeAsync(action);
            }
            else
            {
                await action();
            }
        }
        catch (InvalidOperationException)
        {
            // Fallback for headless environments without active Dispatcher
            await action();
        }
    }
}
