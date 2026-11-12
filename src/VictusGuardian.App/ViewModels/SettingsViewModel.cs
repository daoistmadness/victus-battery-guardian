using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Configuration;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Simulation;
using VictusGuardian.Storage.Repositories;

namespace VictusGuardian.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ICompositeBatteryTelemetryProvider _compositeProvider;
    private readonly ISimulatedBatteryService _simulator;
    private readonly ISettingsService _settingsService;
    private readonly IBatteryHistoryRepository _historyRepository;

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private int _pollingIntervalSeconds = 5;

    [ObservableProperty]
    private double _lowBatteryThreshold = 20.0;

    [ObservableProperty]
    private double _criticalBatteryThreshold = 10.0;

    [ObservableProperty]
    private double _highChargeReminderThreshold = 80.0;

    [ObservableProperty]
    private bool _enableNotifications = true;

    [ObservableProperty]
    private int _historyRetentionDays = 30;

    [ObservableProperty]
    private bool _startMinimized = false;

    // Simulation controls (active when Demo Mode enabled)
    [ObservableProperty]
    private double _simulatedPercentage = 78.5;

    [ObservableProperty]
    private bool _simulatedAcConnected = false;

    [ObservableProperty]
    private double _simulatedDischargeRateWatts = 18.5;

    [ObservableProperty]
    private string _databasePath = "";

    [ObservableProperty]
    private string _settingsStatusMessage = "Settings loaded.";

    public SettingsViewModel(
        ICompositeBatteryTelemetryProvider compositeProvider,
        ISimulatedBatteryService simulator,
        ISettingsService settingsService,
        IBatteryHistoryRepository historyRepository)
    {
        _compositeProvider = compositeProvider;
        _simulator = simulator;
        _settingsService = settingsService;
        _historyRepository = historyRepository;

        // Initialize from persistent settings
        var settings = _settingsService.CurrentSettings;
        _pollingIntervalSeconds = settings.PollingIntervalSeconds;
        _lowBatteryThreshold = settings.LowBatteryWarningThreshold;
        _criticalBatteryThreshold = settings.CriticalBatteryThreshold;
        _highChargeReminderThreshold = settings.HighChargeReminderThreshold;
        _enableNotifications = settings.EnableNotifications;
        _isDemoMode = settings.EnableDemoMode;
        _compositeProvider.IsDemoModeEnabled = _isDemoMode;
        _historyRetentionDays = settings.HistoryRetentionDays;
        _startMinimized = settings.StartMinimized;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DatabasePath = Path.Combine(appData, "VictusGuardian", "battery_history.db");
    }

    partial void OnIsDemoModeChanged(bool value)
    {
        _compositeProvider.IsDemoModeEnabled = value;
        _ = SaveCurrentSettingsAsync();
        SettingsStatusMessage = value
            ? "Demo Mode ENABLED: Telemetry is now simulated for evaluation."
            : "Demo Mode DISABLED: Probing physical hardware telemetry.";
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        await SaveCurrentSettingsAsync();
        SettingsStatusMessage = "Settings successfully saved to disk.";
    }

    private async Task SaveCurrentSettingsAsync()
    {
        var settings = new AppSettings
        {
            PollingIntervalSeconds = PollingIntervalSeconds,
            LowBatteryWarningThreshold = LowBatteryThreshold,
            CriticalBatteryThreshold = CriticalBatteryThreshold,
            HighChargeReminderThreshold = HighChargeReminderThreshold,
            EnableNotifications = EnableNotifications,
            EnableDemoMode = IsDemoMode,
            HistoryRetentionDays = HistoryRetentionDays,
            StartMinimized = StartMinimized
        };

        await _settingsService.SaveSettingsAsync(settings);
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
    public async Task ExportDiagnosticsAsync()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var exportFolder = Path.Combine(appData, "VictusGuardian", "Diagnostics");
            if (!Directory.Exists(exportFolder))
            {
                Directory.CreateDirectory(exportFolder);
            }

            var exportFile = Path.Combine(exportFolder, $"diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
            var sb = new StringBuilder();
            sb.AppendLine("=== VICTUS BATTERY GUARDIAN DIAGNOSTIC LOG ===");
            sb.AppendLine($"Generated UTC:     {DateTime.UtcNow:o}");
            sb.AppendLine($"OS Version:        {Environment.OSVersion}");
            sb.AppendLine($".NET Runtime:      {Environment.Version}");
            sb.AppendLine($"Machine Name:      {Environment.MachineName}");
            sb.AppendLine($"Demo Mode:         {IsDemoMode}");
            sb.AppendLine($"Polling Interval:  {PollingIntervalSeconds}s");
            sb.AppendLine($"Database Path:     {DatabasePath}");
            sb.AppendLine($"Database Exists:   {File.Exists(DatabasePath)}");

            var reading = await _compositeProvider.GetReadingAsync();
            sb.AppendLine();
            sb.AppendLine("--- Current Telemetry Snapshot ---");
            sb.AppendLine($"Provider Source:   {reading.TelemetryProviderSource}");
            sb.AppendLine($"Available Sensors: {reading.AvailableSensors}");
            sb.AppendLine($"Battery %:         {reading.BatteryPercentage}");
            sb.AppendLine($"Charging State:    {reading.ChargingState}");
            sb.AppendLine($"AC Connected:      {reading.IsAcConnected}");
            sb.AppendLine($"Voltage mV:        {reading.VoltageMillivolts}");
            sb.AppendLine($"Rate mW:           {reading.ChargeRateMilliwatts}");
            sb.AppendLine($"Remaining mWh:     {reading.RemainingCapacityMwh}");
            sb.AppendLine($"Full Charge mWh:   {reading.FullChargeCapacityMwh}");
            sb.AppendLine($"Design mWh:        {reading.DesignCapacityMwh}");
            sb.AppendLine($"Cycle Count:       {reading.CycleCount}");
            sb.AppendLine($"Serial Number:     {reading.SerialNumber}");
            sb.AppendLine($"Battery Name:      {reading.BatteryName}");

            await File.WriteAllTextAsync(exportFile, sb.ToString());
            SettingsStatusMessage = $"Diagnostics exported to: {exportFile}";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"Failed to export diagnostics: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ResetDefaults()
    {
        PollingIntervalSeconds = 5;
        LowBatteryThreshold = 20.0;
        CriticalBatteryThreshold = 10.0;
        HighChargeReminderThreshold = 80.0;
        EnableNotifications = true;
        HistoryRetentionDays = 30;
        StartMinimized = false;

        _simulator.ResetToDefaultVictusProfile();
        SimulatedPercentage = 78.5;
        SimulatedAcConnected = false;
        SimulatedDischargeRateWatts = 18.5;

        _ = SaveCurrentSettingsAsync();
        SettingsStatusMessage = "Reset to default HP Victus configuration and saved.";
    }
}
