using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Repositories;

namespace VictusGuardian.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IBatteryTelemetryProvider _telemetryProvider;
    private readonly IBatteryHealthCalculator _healthCalculator;
    private readonly IBatteryAlertEvaluator _alertEvaluator;
    private readonly IBatteryHistoryRepository _historyRepository;
    private CancellationTokenSource? _pollingCts;
    private bool _isDisposed;

    [ObservableProperty]
    private double _batteryPercentage = 0.0;

    [ObservableProperty]
    private string _powerStatusText = "Checking...";

    [ObservableProperty]
    private string _chargingStateText = "Unknown";

    [ObservableProperty]
    private string _chargeRateText = "Unavailable";

    [ObservableProperty]
    private string _remainingCapacityText = "Unavailable";

    [ObservableProperty]
    private string _fullChargeCapacityText = "Unavailable";

    [ObservableProperty]
    private string _designCapacityText = "Unavailable";

    [ObservableProperty]
    private string _voltageText = "Unavailable";

    [ObservableProperty]
    private string _cycleCountText = "Unavailable";

    [ObservableProperty]
    private string _batteryIdentityText = "Probing sensors...";

    [ObservableProperty]
    private string _supportedSensorsText = "Detecting...";

    [ObservableProperty]
    private string _unsupportedSensorsText = "Detecting...";

    [ObservableProperty]
    private string _estimatedTimeRemainingText = "Calculating...";

    [ObservableProperty]
    private string _provenanceBadge = "PROBING TELEMETRY...";

    [ObservableProperty]
    private string _activeProviderTier = "Initializing";

    [ObservableProperty]
    private bool _isAcConnected = false;

    [ObservableProperty]
    private double _healthPercentage = 0.0;

    [ObservableProperty]
    private string _healthRatingText = "Unknown";

    [ObservableProperty]
    private string _lastUpdatedText = "Never";

    [ObservableProperty]
    private string _lastSuccessfulReadText = "Never";

    [ObservableProperty]
    private bool _isStale = false;

    [ObservableProperty]
    private string _staleWarningText = "";

    [ObservableProperty]
    private bool _isUnavailable = false;

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

        StartAutoPolling();
    }

    public void StartAutoPolling(int intervalSeconds = 5)
    {
        _pollingCts?.Cancel();
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(2, intervalSeconds)));
            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    await RefreshTelemetryAsync();
                }
                catch
                {
                    // Non-fatal background tick error
                }
            }
        }, token);
    }

    public void StopAutoPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts = null;
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

            // Update primary readings
            BatteryPercentage = reading.BatteryPercentage.HasValue
                ? reading.BatteryPercentage.Value
                : 0.0;

            IsAcConnected = reading.IsAcConnected;

            PowerStatusText = reading.PowerStatus.HasValue
                ? (reading.PowerStatus.Value == AcPowerStatus.Online ? "AC Adapter Connected" : "On Battery Power")
                : "Power Status Unavailable";

            ChargingStateText = reading.ChargingState.HasValue
                ? reading.ChargingState.Value.ToString()
                : "Unknown";

            ChargeRateText = reading.RateInWatts.HasValue
                ? $"{(reading.ChargeRateMilliwatts.Value < 0 ? "-" : "+")}{reading.RateInWatts.Value:F1} W"
                : "Unavailable";

            RemainingCapacityText = reading.RemainingCapacityMwh.HasValue
                ? $"{reading.RemainingCapacityMwh.Value:N0} mWh"
                : "Unavailable";

            FullChargeCapacityText = reading.FullChargeCapacityMwh.HasValue
                ? $"{reading.FullChargeCapacityMwh.Value:N0} mWh"
                : "Unavailable";

            DesignCapacityText = reading.DesignCapacityMwh.HasValue
                ? $"{reading.DesignCapacityMwh.Value:N0} mWh"
                : "Unavailable";

            VoltageText = reading.VoltageInVolts.HasValue
                ? $"{reading.VoltageInVolts.Value:F2} V"
                : "Unavailable";

            CycleCountText = reading.CycleCount.HasValue
                ? $"{reading.CycleCount.Value} cycles"
                : "Unavailable";

            // Battery Identity
            var idBuilder = new StringBuilder();
            if (reading.BatteryName.HasValue) idBuilder.Append(reading.BatteryName.Value);
            if (reading.Manufacturer.HasValue)
            {
                if (idBuilder.Length > 0) idBuilder.Append(" / ");
                idBuilder.Append(reading.Manufacturer.Value);
            }
            if (reading.Chemistry.HasValue && reading.Chemistry.Value != "Unknown")
            {
                if (idBuilder.Length > 0) idBuilder.Append($" ({reading.Chemistry.Value})");
            }
            if (reading.SerialNumber.HasValue)
            {
                if (idBuilder.Length > 0) idBuilder.Append($" - S/N: {reading.SerialNumber.Value}");
            }
            BatteryIdentityText = idBuilder.Length > 0 ? idBuilder.ToString() : "Generic ACPI Battery";

            // Sensor capability breakdown
            FormatSensorCapabilities(reading.AvailableSensors);

            EstimatedTimeRemainingText = reading.EstimatedTimeRemaining.HasValue
                ? $"{(int)reading.EstimatedTimeRemaining.Value.TotalHours}h {reading.EstimatedTimeRemaining.Value.Minutes}m remaining"
                : (reading.ChargingState.Value == BatteryChargingState.Full ? "Fully Charged" : "Calculating...");

            ActiveProviderTier = reading.TelemetryProviderSource;

            IsUnavailable = reading.AvailableSensors == SensorCapabilities.None && !reading.IsSimulated;

            if (reading.IsSimulated)
            {
                ProvenanceBadge = "SIMULATED TELEMETRY (DEMO MODE)";
            }
            else if (IsUnavailable)
            {
                ProvenanceBadge = "TELEMETRY UNAVAILABLE";
            }
            else
            {
                ProvenanceBadge = $"LIVE HARDWARE ({reading.TelemetryProviderSource})";
            }

            IsStale = reading.IsStale;
            StaleWarningText = IsStale
                ? "Warning: Telemetry readings are stale. Device may be in low-power idle or polling throttled."
                : "";

            HealthPercentage = health.HealthPercentage;
            HealthRatingText = health.Rating.ToString();
            LastUpdatedText = DateTime.Now.ToString("HH:mm:ss");

            if (reading.AvailableSensors != SensorCapabilities.None)
            {
                LastSuccessfulReadText = reading.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            }

            ActiveAlerts.Clear();
            foreach (var alert in alerts)
            {
                ActiveAlerts.Add($"[{alert.Level}] {alert.Title}: {alert.Message}");
            }

            // Save to local storage (idle throttled)
            if (reading.BatteryPercentage.HasValue)
            {
                await _historyRepository.RecordReadingAsync(reading);
            }
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

    private void FormatSensorCapabilities(SensorCapabilities sensors)
    {
        var supported = new StringBuilder();
        var unsupported = new StringBuilder();

        void Check(SensorCapabilities cap, string name)
        {
            if ((sensors & cap) != 0)
            {
                if (supported.Length > 0) supported.Append(", ");
                supported.Append(name);
            }
            else
            {
                if (unsupported.Length > 0) unsupported.Append(", ");
                unsupported.Append(name);
            }
        }

        Check(SensorCapabilities.Percentage, "Percentage");
        Check(SensorCapabilities.PowerStatus, "AC Status");
        Check(SensorCapabilities.ChargingState, "Charging State");
        Check(SensorCapabilities.RemainingCapacity, "Remaining Capacity");
        Check(SensorCapabilities.FullChargeCapacity, "Full Charge Capacity");
        Check(SensorCapabilities.DesignCapacity, "Design Capacity");
        Check(SensorCapabilities.ChargeRate, "Charge/Discharge Rate");
        Check(SensorCapabilities.Voltage, "Voltage");
        Check(SensorCapabilities.CycleCount, "Cycle Count");
        Check(SensorCapabilities.BatteryIdentity, "Identity/Serial");

        SupportedSensorsText = supported.Length > 0 ? supported.ToString() : "None";
        UnsupportedSensorsText = unsupported.Length > 0 ? unsupported.ToString() : "None";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopAutoPolling();
    }
}
