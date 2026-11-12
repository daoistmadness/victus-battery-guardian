using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using VictusGuardian.App.ViewModels;
using VictusGuardian.App.Views;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Simulation;
using VictusGuardian.Storage.Repositories;
using VictusGuardian.Windows;

namespace VictusGuardian.App;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // 1. Core Services
        services.AddSingleton<IBatteryHealthCalculator, DefaultBatteryHealthCalculator>();
        services.AddSingleton<IBatteryAlertEvaluator, DefaultBatteryAlertEvaluator>();
        services.AddSingleton<SimulatedBatteryTelemetryProvider>();
        services.AddSingleton<ISimulatedBatteryService>(sp => sp.GetRequiredService<SimulatedBatteryTelemetryProvider>());
        // Milestone 1 uses Simulated provider for Demo Mode
        services.AddSingleton<IBatteryTelemetryProvider>(sp => sp.GetRequiredService<SimulatedBatteryTelemetryProvider>());

        // 2. Storage Services
        services.AddSingleton<IBatteryHistoryRepository, SqliteBatteryHistoryRepository>();

        // 3. Windows Services
        services.AddWindowsGuardianServices();

        // 4. ViewModels
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<HealthViewModel>();
        services.AddSingleton<ChargingViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        // Initialize SQLite schema
        var storage = Services.GetRequiredService<IBatteryHistoryRepository>();
        try
        {
            await storage.InitializeAsync();
        }
        catch (Exception)
        {
            // Fallback for restricted storage environments
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowVm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainWindowVm
            };

            // Trigger initial data load
            _ = mainWindowVm.Dashboard.RefreshTelemetryAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
