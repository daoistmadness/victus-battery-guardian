using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using VictusGuardian.App.Diagnostics;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Configuration;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Models;
using VictusGuardian.Storage.Repositories;
using VictusGuardian.App.Notifications;

namespace VictusGuardian.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly IBatteryTelemetryProvider _telemetryProvider;
    private readonly IBatteryHealthCalculator _healthCalculator;
    private readonly IBatteryAlertEvaluator _alertEvaluator;
    private readonly IBatteryHistoryRepository _historyRepository;
    private readonly ISettingsService _settingsService;
    private readonly IAppNotificationCoordinator? _notificationCoordinator;
    private CancellationTokenSource? _pollingCts;
    private bool _isDisposed;
    private volatile bool _isWindowVisible = true;
    private volatile bool _lastReadingWasOnAc;
    private int _isRefreshInProgress;
    private int _refreshRequested;
    private int _pollingIntervalSeconds = 30;
    private bool _hasCachedUsageStats;
    private DateTimeOffset _usageStatsCacheExpiresUtc;
    private TimeSpan? _cachedHistoricalRuntime;
    private bool _hasRecordedHealthObservation;
    private double _lastHealthObservationFullCapacityMwh;
    private int? _lastHealthObservationCycleCount;
    private string? _lastHealthObservationSerialNumber;
    private DateTimeOffset _healthObservationRetryAfterUtc;

    private static readonly TimeSpan UsageStatsCacheDuration = TimeSpan.FromMinutes(10);

    public bool IsWindowVisible => _isWindowVisible;
    public event Action<BatteryReading>? ReadingUpdated;

    public void SetWindowVisible(bool isVisible)
    {
        if (_isWindowVisible != isVisible)
        {
            _isWindowVisible = isVisible;
            if (_pollingCts != null)
            {
                StartAutoPolling(_pollingIntervalSeconds);
            }
            if (isVisible)
            {
                _ = RefreshTelemetryAsync();
            }
            else
            {
                IsBusy = false;
            }
        }
    }

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

    private ChargingSessionRecord? _cachedActiveSession;
    private bool _hasInitializedActiveSession = false;

    public ObservableCollection<string> ActiveAlerts { get; } = new();

    public DashboardViewModel(
        IBatteryTelemetryProvider telemetryProvider,
        IBatteryHealthCalculator healthCalculator,
        IBatteryAlertEvaluator alertEvaluator,
        IBatteryHistoryRepository historyRepository,
        ISettingsService settingsService,
        IAppNotificationCoordinator? notificationCoordinator = null)
    {
        _telemetryProvider = telemetryProvider;
        _healthCalculator = healthCalculator;
        _alertEvaluator = alertEvaluator;
        _historyRepository = historyRepository;
        _settingsService = settingsService;
        _notificationCoordinator = notificationCoordinator;

        // Auto-polling is explicitly NOT started during ViewModel construction
        // to ensure the MainWindow is rendered before background work begins.
    }

    public void StartAutoPolling(int? intervalSeconds = null)
    {
        _pollingCts?.Cancel();
        var pollingCts = new CancellationTokenSource();
        _pollingCts = pollingCts;
        var token = pollingCts.Token;
        var interval = intervalSeconds ?? _settingsService.CurrentSettings.PollingIntervalSeconds;
        interval = Math.Clamp(interval, 5, 120);
        _pollingIntervalSeconds = interval;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var effectiveInterval = _isWindowVisible
                            ? interval
                            : Math.Max(interval, _lastReadingWasOnAc ? 60 : 120);
                        await Task.Delay(TimeSpan.FromSeconds(effectiveInterval), token);
                        await SafeInvokeOnUIThreadAsync(RefreshTelemetryAsync);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogWarning($"Auto-polling tick exception: {ex.Message}");
                    }
                }
            }
            finally
            {
                pollingCts.Dispose();
            }
        });
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
            // Fallback for headless test environments without active Dispatcher
            await action();
        }
    }

    public void StopAutoPolling()
    {
        _pollingCts?.Cancel();
        _pollingCts = null;
    }

    [RelayCommand]
    public async Task RefreshTelemetryAsync()
    {
        if (Interlocked.Exchange(ref _isRefreshInProgress, 1) != 0)
        {
            Interlocked.Exchange(ref _refreshRequested, 1);
            return;
        }
        if (_isWindowVisible) IsBusy = true;

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
                var isCharging = reading.ChargingState.HasValue && reading.ChargingState.Value == BatteryChargingState.Charging;

                if (!_hasInitializedActiveSession)
                {
                    _cachedActiveSession = await _historyRepository.GetActiveChargingSessionAsync();
                    _hasInitializedActiveSession = true;
                }

                if (isCharging)
                {
                    if (_cachedActiveSession == null && reading.BatteryPercentage.HasValue)
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
                        var id = await _historyRepository.StartChargingSessionAsync(newSession);
                        _cachedActiveSession = newSession with { Id = id };
                    }
                }
                else if (_cachedActiveSession != null)
                {
                    var endPct = reading.BatteryPercentage.HasValue ? reading.BatteryPercentage.Value : _cachedActiveSession.StartPercentage;
                    var endEnergy = reading.RemainingCapacityMwh.HasValue ? (double?)reading.RemainingCapacityMwh.Value : null;
                    await _historyRepository.CompleteChargingSessionAsync(_cachedActiveSession.Id, reading.Timestamp, endPct, endEnergy);
                    _cachedActiveSession = null;
                }
            }
            catch
            {
                // Session tracking non-fatal
            }

            // Battery Health Tracking
            try
            {
                if (reading.FullChargeCapacityMwh.HasValue && reading.DesignCapacityMwh.HasValue &&
                    DateTimeOffset.UtcNow >= _healthObservationRetryAfterUtc)
                {
                    var fullCapacity = reading.FullChargeCapacityMwh.Value;
                    int? cycleCount = reading.CycleCount.HasValue ? reading.CycleCount.Value : null;
                    string? serialNumber = reading.SerialNumber.HasValue ? reading.SerialNumber.Value : null;
                    var hasChanged = !_hasRecordedHealthObservation ||
                        Math.Abs(_lastHealthObservationFullCapacityMwh - fullCapacity) >= 1.0 ||
                        _lastHealthObservationCycleCount != cycleCount ||
                        !string.Equals(_lastHealthObservationSerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase);

                    if (hasChanged)
                    {
                        var healthRecord = new BatteryHealthRecord
                        {
                            Timestamp = reading.Timestamp,
                            FullChargeCapacityMwh = fullCapacity,
                            DesignCapacityMwh = reading.DesignCapacityMwh.Value,
                            CycleCount = cycleCount,
                            BatterySerialNumber = serialNumber,
                            BatteryName = reading.BatteryName.HasValue ? reading.BatteryName.Value : null,
                            Chemistry = reading.Chemistry.HasValue ? reading.Chemistry.Value : null
                        };
                        await _historyRepository.RecordHealthObservationAsync(healthRecord);
                        _lastHealthObservationFullCapacityMwh = fullCapacity;
                        _lastHealthObservationCycleCount = cycleCount;
                        _lastHealthObservationSerialNumber = serialNumber;
                        _hasRecordedHealthObservation = true;
                        _healthObservationRetryAfterUtc = DateTimeOffset.MinValue;
                    }
                }
            }
            catch
            {
                // Health tracking non-fatal
                _healthObservationRetryAfterUtc = DateTimeOffset.UtcNow.AddMinutes(5);
            }

            // Notify status listeners / tray icon
            _lastReadingWasOnAc = reading.IsAcConnected;
            ReadingUpdated?.Invoke(reading);

            // Keep alerts and notifications current while the dashboard is hidden.
            var alertSettings = new AlertRuleSettings
            {
                LowBatteryPercentage = _settingsService.CurrentSettings.LowBatteryWarningThreshold,
                CriticalBatteryPercentage = _settingsService.CurrentSettings.CriticalBatteryThreshold,
                MaxChargeTargetPercentage = _settingsService.CurrentSettings.HighChargeReminderThreshold,
                EnableNotifications = _settingsService.CurrentSettings.EnableNotifications
            };

            var alerts = _alertEvaluator.Evaluate(reading, health, alertSettings);
            if (_notificationCoordinator != null && alerts.Count > 0)
            {
                foreach (var alert in alerts)
                {
                    _ = _notificationCoordinator.DispatchAlertAsync(alert);
                }
            }

            if (!_isWindowVisible)
            {
                return;
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
                        if (!_hasCachedUsageStats || DateTimeOffset.UtcNow >= _usageStatsCacheExpiresUtc)
                        {
                            var stats = await _historyRepository.GetUsageStatisticsAsync(TimeSpan.FromHours(24));
                            _cachedHistoricalRuntime = stats.EstimatedRuntimeFromHistory;
                            _usageStatsCacheExpiresUtc = DateTimeOffset.UtcNow + UsageStatsCacheDuration;
                            _hasCachedUsageStats = true;
                        }

                        if (_cachedHistoricalRuntime.HasValue)
                        {
                            var histTs = _cachedHistoricalRuntime.Value;
                            EstimatedTimeRemainingText = $"~{(int)histTs.TotalHours}h {histTs.Minutes}m (Estimated from usage history)";
                        }
                        else
                        {
                            EstimatedTimeRemainingText = "Insufficient data";
                        }
                    }
                    catch
                    {
                        _cachedHistoricalRuntime = null;
                        _usageStatsCacheExpiresUtc = DateTimeOffset.UtcNow + UsageStatsCacheDuration;
                        _hasCachedUsageStats = true;
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
            HealthRatingText = $"Reported Capacity Retention: {health.HealthPercentage:F0}% ({health.Rating})";

            ActiveAlerts.Clear();
            if (alerts.Count > 0)
            {
                foreach (var alert in alerts)
                {
                    ActiveAlerts.Add($"⚠ [{alert.Level}] {alert.Title}: {alert.Message}");
                }
            }
            else if (reading.IsAcConnected && reading.BatteryPercentage.HasValue &&
                     reading.BatteryPercentage.Value >= alertSettings.MaxChargeTargetPercentage)
            {
                ActiveAlerts.Add($"ℹ High Charge Advisory: Battery is at {reading.BatteryPercentage.Value:0}% on AC power (configured target: {alertSettings.MaxChargeTargetPercentage:0}%). Disconnecting AC adapter preserves lithium cell longevity. (Note: Victus Guardian provides advisory guidance and does not alter hardware BIOS charging limits).");
            }
            else
            {
                ActiveAlerts.Add("✓ Battery system operating normally. No active warnings.");
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
            if (_isWindowVisible)
            {
                EstimatedTimeRemainingText = "Unavailable";
                ActiveAlerts.Clear();
                ActiveAlerts.Add($"❌ Telemetry read failure: {ex.Message}");
            }
        }
        finally
        {
            if (_isWindowVisible) IsBusy = false;
            Volatile.Write(ref _isRefreshInProgress, 0);
            if (Interlocked.Exchange(ref _refreshRequested, 0) != 0 && !_isDisposed)
            {
                _ = SafeInvokeOnUIThreadAsync(RefreshTelemetryAsync);
            }
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
