using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Configuration;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Models;
using VictusGuardian.Storage.Repositories;

namespace VictusGuardian.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IBatteryTelemetryProvider _telemetryProvider;
    private readonly IBatteryHealthCalculator _healthCalculator;
    private readonly IBatteryAlertEvaluator _alertEvaluator;
    private readonly IBatteryHistoryRepository _historyRepository;
    private readonly ISettingsService _settingsService;
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
    private string _estimatedTimeRemainingText = "Checking...";

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
        IBatteryHistoryRepository historyRepository,
        ISettingsService settingsService)
    {
        _telemetryProvider = telemetryProvider;
        _healthCalculator = healthCalculator;
        _alertEvaluator = alertEvaluator;
        _historyRepository = historyRepository;
        _settingsService = settingsService;

        StartAutoPolling(_settingsService.CurrentSettings.PollingIntervalSeconds);
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

            // Record to SQLite history
            try
            {
                await _historyRepository.RecordReadingAsync(reading);
            }
            catch
            {
                // Non-fatal persistence error
            }

            // Charging Session Tracking
            try
            {
                var activeSession = await _historyRepository.GetActiveChargingSessionAsync();
                var isCharging = reading.ChargingState.HasValue && reading.ChargingState.Value == BatteryChargingState.Charging;

                if (isCharging)
                {
                    if (activeSession == null && reading.BatteryPercentage.HasValue)
                    {
                        var newSession = new ChargingSessionRecord
                        {
                            StartTime = reading.Timestamp,
                            StartPercentage = reading.BatteryPercentage.Value,
                            StartEnergyMwh = reading.RemainingCapacityMwh.HasValue ? (double?)reading.RemainingCapacityMwh.Value : null,
                            ProviderSource = reading.TelemetryProviderSource,
                            BatterySerialNumber = reading.SerialNumber.HasValue ? reading.SerialNumber.Value : null,
                            IsCompleted = false
                        };
                        await _historyRepository.StartChargingSessionAsync(newSession);
                    }
                }
                else if (activeSession != null)
                {
                    var endPct = reading.BatteryPercentage.HasValue ? reading.BatteryPercentage.Value : activeSession.StartPercentage;
                    var endEnergy = reading.RemainingCapacityMwh.HasValue ? (double?)reading.RemainingCapacityMwh.Value : null;
                    await _historyRepository.CompleteChargingSessionAsync(activeSession.Id, reading.Timestamp, endPct, endEnergy);
                }
            }
            catch
            {
                // Session tracking non-fatal
            }

            // Battery Health Tracking
            try
            {
                if (reading.FullChargeCapacityMwh.HasValue && reading.DesignCapacityMwh.HasValue)
                {
                    var healthRecord = new BatteryHealthRecord
                    {
                        Timestamp = reading.Timestamp,
                        FullChargeCapacityMwh = reading.FullChargeCapacityMwh.Value,
                        DesignCapacityMwh = reading.DesignCapacityMwh.Value,
                        CycleCount = reading.CycleCount.HasValue ? reading.CycleCount.Value : null,
                        BatterySerialNumber = reading.SerialNumber.HasValue ? reading.SerialNumber.Value : null,
                        BatteryName = reading.BatteryName.HasValue ? reading.BatteryName.Value : null,
                        Chemistry = reading.Chemistry.HasValue ? reading.Chemistry.Value : null
                    };
                    await _historyRepository.RecordHealthObservationAsync(healthRecord);
                }
            }
            catch
            {
                // Health tracking non-fatal
            }

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
                : "Unavailable (Sensor not exposed by hardware)";

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

            // Estimated Time Remaining calculation
            if (reading.ChargingState.HasValue && reading.ChargingState.Value == BatteryChargingState.Full)
            {
                EstimatedTimeRemainingText = "Fully Charged";
            }
            else if (reading.IsStale)
            {
                EstimatedTimeRemainingText = "Waiting for fresh telemetry";
            }
            else if (reading.ChargingState.HasValue && reading.ChargingState.Value == BatteryChargingState.Charging)
            {
                if (reading.RateInWatts.HasValue && reading.RateInWatts.Value > 0.1 &&
                    reading.FullChargeCapacityMwh.HasValue && reading.RemainingCapacityMwh.HasValue)
                {
                    var neededMwh = Math.Max(0.0, reading.FullChargeCapacityMwh.Value - reading.RemainingCapacityMwh.Value);
                    var rateMwh = reading.RateInWatts.Value * 1000.0;
                    var hours = neededMwh / rateMwh;
                    var ts = TimeSpan.FromHours(hours);
                    EstimatedTimeRemainingText = $"{(int)ts.TotalHours}h {ts.Minutes}m to full";
                }
                else
                {
                    EstimatedTimeRemainingText = "Unavailable (Charging rate unsupported)";
                }
            }
            else if (reading.ChargingState.HasValue && reading.ChargingState.Value == BatteryChargingState.Discharging)
            {
                if (reading.EstimatedTimeRemaining.HasValue)
                {
                    var ts = reading.EstimatedTimeRemaining.Value;
                    EstimatedTimeRemainingText = $"{(int)ts.TotalHours}h {ts.Minutes}m remaining";
                }
                else
                {
                    // Fallback to usage history
                    try
                    {
                        var stats = await _historyRepository.GetUsageStatisticsAsync(TimeSpan.FromHours(24));
                        if (stats.EstimatedRuntimeFromHistory.HasValue)
                        {
                            var histTs = stats.EstimatedRuntimeFromHistory.Value;
                            EstimatedTimeRemainingText = $"~{(int)histTs.TotalHours}h {histTs.Minutes}m (Estimated from usage history)";
                        }
                        else
                        {
                            EstimatedTimeRemainingText = "Insufficient data";
                        }
                    }
                    catch
                    {
                        EstimatedTimeRemainingText = "Insufficient data";
                    }
                }
            }
            else if (reading.IsAcConnected)
            {
                EstimatedTimeRemainingText = "Idle / Holding Charge";
            }
            else
            {
                EstimatedTimeRemainingText = "Unavailable";
            }

            ActiveProviderTier = reading.TelemetryProviderSource;

            IsUnavailable = reading.AvailableSensors == SensorCapabilities.None && !reading.IsSimulated;

            if (reading.IsSimulated)
            {
                ProvenanceBadge = "SIMULATED TELEMETRY (DEMO MODE)";
            }
            else if (reading.TelemetryProviderSource.Contains("IOCTL"))
            {
                ProvenanceBadge = "HARDWARE IOCTL (DIRECT DEVICE)";
            }
            else if (reading.TelemetryProviderSource.Contains("GetSystemPowerStatus"))
            {
                ProvenanceBadge = "WIN32 API (FALLBACK TIER)";
            }
            else
            {
                ProvenanceBadge = "TELEMETRY UNAVAILABLE";
            }

            HealthPercentage = health.HealthPercentage;
            HealthRatingText = health.Rating.ToString();

            // Alerts evaluation
            var alertSettings = new AlertRuleSettings
            {
                LowBatteryPercentage = _settingsService.CurrentSettings.LowBatteryWarningThreshold,
                CriticalBatteryPercentage = _settingsService.CurrentSettings.CriticalBatteryThreshold,
                MaxChargeTargetPercentage = _settingsService.CurrentSettings.HighChargeReminderThreshold,
                EnableNotifications = _settingsService.CurrentSettings.EnableNotifications
            };

            var alerts = _alertEvaluator.Evaluate(reading, health, alertSettings);
            ActiveAlerts.Clear();
            if (alerts.Count == 0)
            {
                ActiveAlerts.Add("✓ Battery system operating normally. No active warnings.");
            }
            else
            {
                foreach (var alert in alerts)
                {
                    ActiveAlerts.Add($"⚠ [{alert.Level}] {alert.Title}: {alert.Message}");
                }
            }

            // Stale telemetry handling
            IsStale = reading.IsStale;
            StaleWarningText = reading.IsStale
                ? "WARNING: Battery telemetry has not refreshed recently. Sensor readings may be delayed."
                : "";

            LastUpdatedText = DateTimeOffset.Now.ToString("HH:mm:ss");
            if (!reading.IsStale && reading.AvailableSensors != SensorCapabilities.None)
            {
                LastSuccessfulReadText = LastUpdatedText;
            }
        }
        catch (Exception ex)
        {
            EstimatedTimeRemainingText = "Unavailable";
            ActiveAlerts.Clear();
            ActiveAlerts.Add($"❌ Telemetry read failure: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void FormatSensorCapabilities(SensorCapabilities caps)
    {
        var supported = new StringBuilder();
        var unsupported = new StringBuilder();

        void Check(SensorCapabilities flag, string name)
        {
            if ((caps & flag) != 0)
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
        Check(SensorCapabilities.BatteryIdentity, "Device Identity");

        SupportedSensorsText = supported.Length > 0 ? supported.ToString() : "None";
        UnsupportedSensorsText = unsupported.Length > 0 ? unsupported.ToString() : "None";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopAutoPolling();
        GC.SuppressFinalize(this);
    }
}
