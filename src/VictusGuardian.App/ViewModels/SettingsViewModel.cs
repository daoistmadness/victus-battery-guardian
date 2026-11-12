using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Simulation;

namespace VictusGuardian.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ICompositeBatteryTelemetryProvider _compositeProvider;
    private readonly ISimulatedBatteryService _simulator;

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private double _simulatedPercentage = 78.5;

    [ObservableProperty]
    private bool _simulatedAcConnected = false;

    [ObservableProperty]
    private double _simulatedDischargeRateWatts = 18.5;

    [ObservableProperty]
    private int _pollingIntervalSeconds = 5;

    [ObservableProperty]
    private string _databasePath = "";

    [ObservableProperty]
    private string _settingsStatusMessage = "Settings loaded.";

    public SettingsViewModel(
        ICompositeBatteryTelemetryProvider compositeProvider,
        ISimulatedBatteryService simulator)
    {
        _compositeProvider = compositeProvider;
        _simulator = simulator;

        _isDemoMode = _compositeProvider.IsDemoModeEnabled;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DatabasePath = Path.Combine(appData, "VictusGuardian", "battery_history.db");
    }

    partial void OnIsDemoModeChanged(bool value)
    {
        _compositeProvider.IsDemoModeEnabled = value;
        SettingsStatusMessage = value
            ? "Demo Mode ENABLED: Telemetry is now simulated for evaluation."
            : "Demo Mode DISABLED: Probing physical hardware telemetry.";
    }

    [RelayCommand]
    public void ApplySimulationSettings()
    {
        _simulator.SetPercentage(SimulatedPercentage);
        _simulator.SetAcConnected(SimulatedAcConnected);
        if (SimulatedAcConnected)
        {
            _simulator.SetChargeRateWatts(SimulatedDischargeRateWatts);
        }
        else
        {
            _simulator.SetDischargeRateWatts(SimulatedDischargeRateWatts);
        }

        SettingsStatusMessage = $"Applied simulated state: {SimulatedPercentage:F0}% | AC: {(SimulatedAcConnected ? "ON" : "OFF")} | Rate: {SimulatedDischargeRateWatts:F1}W";
    }

    [RelayCommand]
    public void ResetDefaults()
    {
        _simulator.ResetToDefaultVictusProfile();
        SimulatedPercentage = 78.5;
        SimulatedAcConnected = false;
        SimulatedDischargeRateWatts = 18.5;
        SettingsStatusMessage = "Reset to default HP Victus 70 Wh simulation profile.";
    }
}
