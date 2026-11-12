using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VictusGuardian.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
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
    private bool _isDemoModeActive = true;

    [ObservableProperty]
    private string _appVersionText = "v0.1.0-alpha (Milestone 1)";

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        HealthViewModel health,
        ChargingViewModel charging,
        HistoryViewModel history,
        SettingsViewModel settings)
    {
        Dashboard = dashboard;
        Health = health;
        Charging = charging;
        History = history;
        Settings = settings;

        _currentView = dashboard;
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
