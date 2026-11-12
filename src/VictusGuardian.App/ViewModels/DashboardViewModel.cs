using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Repositories;

namespace VictusGuardian.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IBatteryTelemetryProvider _telemetryProvider;
    private readonly IBatteryHealthCalculator _healthCalculator;
    private readonly IBatteryAlertEvaluator _alertEvaluator;
    private readonly IBatteryHistoryRepository _historyRepository;

    [ObservableProperty]
    private double _batteryPercentage = 78.5;

    [ObservableProperty]
    private string _powerStatusText = "Battery (AC Offline)";

    [ObservableProperty]
    private string _chargingStateText = "Discharging";

    [ObservableProperty]
    private string _chargeRateText = "18.5 W";

    [ObservableProperty]
    private string _remainingCapacityText = "52,202 mWh";

    [ObservableProperty]
    private string _estimatedTimeRemainingText = "2h 49m remaining";

    [ObservableProperty]
    private string _provenanceBadge = "SIMULATED TELEMETRY";

    [ObservableProperty]
    private bool _isAcConnected = false;

    [ObservableProperty]
    private double _healthPercentage = 95.0;

    [ObservableProperty]
    private string _healthRatingText = "Excellent";

    [ObservableProperty]
    private string _lastUpdatedText = "Just now";

    [ObservableProperty]
    private bool _isBusy = false;

    public ObservableCollection<string> ActiveAlerts { get; } = new();

    public DashboardViewModel(
        IBatteryTelemetryProvider telemetryProvider,
        IBatteryHealthCalculator healthCalculator,
        IBatteryAlertEvaluator alertEvaluator,
        IBatteryHistoryRepository historyRepository)
    {
        _telemetryProvider = telemetryProvider;
        _healthCalculator = healthCalculator;
        _alertEvaluator = alertEvaluator;
        _historyRepository = historyRepository;
    }

    [RelayCommand]
    public async Task RefreshTelemetryAsync()
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            var reading = await _telemetryProvider.GetReadingAsync();
            var health = _healthCalculator.Calculate(reading);
            var alerts = _alertEvaluator.Evaluate(reading, health);

            // Update UI properties
            BatteryPercentage = reading.BatteryPercentage.HasValue
                ? reading.BatteryPercentage.Value
                : 0.0;

            IsAcConnected = reading.IsAcConnected;

            PowerStatusText = reading.PowerStatus.HasValue
                ? (reading.PowerStatus.Value == AcPowerStatus.Online ? "AC Adapter Connected" : "On Battery Power")
                : "Power Status Unknown";

            ChargingStateText = reading.ChargingState.HasValue
                ? reading.ChargingState.Value.ToString()
                : "Unknown";

            ChargeRateText = reading.RateInWatts.HasValue
                ? $"{reading.RateInWatts.Value:F1} W"
                : "Unavailable";

            RemainingCapacityText = reading.RemainingCapacityMwh.HasValue
                ? $"{reading.RemainingCapacityMwh.Value:N0} mWh"
                : "Unavailable";

            EstimatedTimeRemainingText = reading.EstimatedTimeRemaining.HasValue
                ? $"{(int)reading.EstimatedTimeRemaining.Value.TotalHours}h {reading.EstimatedTimeRemaining.Value.Minutes}m remaining"
                : (reading.ChargingState.Value == BatteryChargingState.Full ? "Fully Charged" : "Calculating...");

            ProvenanceBadge = reading.IsSimulated
                ? "SIMULATED TELEMETRY (DEMO MODE)"
                : "LIVE HARDWARE TELEMETRY";

            HealthPercentage = health.HealthPercentage;
            HealthRatingText = health.Rating.ToString();
            LastUpdatedText = DateTime.Now.ToString("HH:mm:ss");

            ActiveAlerts.Clear();
            foreach (var alert in alerts)
            {
                ActiveAlerts.Add($"[{alert.Level}] {alert.Title}: {alert.Message}");
            }

            // Save to local storage (idle throttled)
            await _historyRepository.RecordReadingAsync(reading);
        }
        catch (Exception ex)
        {
            ActiveAlerts.Clear();
            ActiveAlerts.Add($"Error refreshing telemetry: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
