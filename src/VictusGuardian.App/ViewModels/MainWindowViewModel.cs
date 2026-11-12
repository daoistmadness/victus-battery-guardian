using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Contracts;

namespace VictusGuardian.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ICompositeBatteryTelemetryProvider _compositeProvider;

    public DashboardViewModel Dashboard { get; }
    public HealthViewModel Health { get; }
    public ChargingViewModel Charging { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _selectedSection = "Dashboard";

    [ObservableProperty]
    private string _appVersionText = "v0.2.0-beta (Milestone 2)";

    public bool IsDemoModeActive => _compositeProvider.IsDemoModeEnabled;

    public string ProviderStatusBanner
    {
        get
        {
            if (_compositeProvider.IsDemoModeEnabled)
                return "⚠ DEMO MODE ACTIVE — Battery telemetry is simulated for evaluation.";
            if (Dashboard.IsUnavailable)
                return "⚠ TELEMETRY UNAVAILABLE — No battery hardware detected. Enable Demo Mode in Settings to test.";
            if (Dashboard.IsStale)
                return "⚠ STALE TELEMETRY — Battery sensor readings are delayed.";
            return $"✓ LIVE HARDWARE TELEMETRY ({Dashboard.ActiveProviderTier})";
        }
    }

    public MainWindowViewModel(
        ICompositeBatteryTelemetryProvider compositeProvider,
        DashboardViewModel dashboard,
        HealthViewModel health,
        ChargingViewModel charging,
        HistoryViewModel history,
        SettingsViewModel settings)
    {
        _compositeProvider = compositeProvider;
        Dashboard = dashboard;
        Health = health;
        Charging = charging;
        History = history;
        Settings = settings;

        _currentView = dashboard;

        // When dashboard properties change, update ProviderStatusBanner
        Dashboard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Dashboard.ActiveProviderTier) or
                nameof(Dashboard.IsUnavailable) or
                nameof(Dashboard.IsStale))
            {
                OnPropertyChanged(nameof(ProviderStatusBanner));
            }
        };

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Settings.IsDemoMode))
            {
                OnPropertyChanged(nameof(IsDemoModeActive));
                OnPropertyChanged(nameof(ProviderStatusBanner));
                _ = Dashboard.RefreshTelemetryAsync();
            }
        };
    }

    [RelayCommand]
    public async Task NavigateToAsync(string section)
    {
        SelectedSection = section;

        CurrentView = section switch
        {
            "Dashboard" => Dashboard,
            "Health" => Health,
            "Charging" => Charging,
            "History" => History,
            "Settings" => Settings,
            _ => Dashboard
        };

        if (section == "Dashboard")
        {
            await Dashboard.RefreshTelemetryAsync();
        }
        else if (section == "Health")
        {
            await Health.RefreshHealthAsync();
        }
        else if (section == "Charging")
        {
            await Charging.RefreshChargingAsync();
        }
        else if (section == "History")
        {
            await History.LoadHistoryAsync();
        }
    }
}
