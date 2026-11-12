using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Storage.Models;
using VictusGuardian.Storage.Repositories;

namespace VictusGuardian.App.ViewModels;

public partial class HealthViewModel : ViewModelBase
{
    private readonly IBatteryTelemetryProvider _telemetryProvider;
    private readonly IBatteryHealthCalculator _healthCalculator;
    private readonly IBatteryHistoryRepository _historyRepository;

    [ObservableProperty]
    private double _healthPercentage = 0.0;

    [ObservableProperty]
    private double _wearPercentage = 0.0;

    [ObservableProperty]
    private string _ratingText = "Unknown";

    [ObservableProperty]
    private string _cycleCountText = "Probing...";

    [ObservableProperty]
    private string _fullChargeCapacityText = "Unavailable";

    [ObservableProperty]
    private string _designCapacityText = "Unavailable";

    [ObservableProperty]
    private string _reportedRatioText = "Unavailable";

    [ObservableProperty]
    private string _chemistryText = "Lithium-Ion";

    [ObservableProperty]
    private string _provenanceNotice = "";

    [ObservableProperty]
    private string _firmwareEqualityNotice = "";

    [ObservableProperty]
    private bool _hasFirmwareEqualityWarning = false;

    [ObservableProperty]
    private string _healthTrendText = "Stable";

    [ObservableProperty]
    private string _reportingLimitationsText = "Battery wear calculation is based on the ratio of Full Charge Capacity to Factory Design Capacity reported by the battery micro-controller. A 100% capacity ratio indicates current firmware report equality and does not guarantee zero physical chemical degradation.";

    [ObservableProperty]
    private string _lastUpdatedText = "Never";

    public ObservableCollection<BatteryHealthRecord> HistoricalObservations { get; } = new();

    public HealthViewModel(
        IBatteryTelemetryProvider telemetryProvider,
        IBatteryHealthCalculator healthCalculator,
        IBatteryHistoryRepository historyRepository)
    {
        _telemetryProvider = telemetryProvider;
        _healthCalculator = healthCalculator;
        _historyRepository = historyRepository;
    }

    [RelayCommand]
    public async Task RefreshHealthAsync()
    {
        var reading = await _telemetryProvider.GetReadingAsync();
        var health = _healthCalculator.Calculate(reading);

        HealthPercentage = health.HealthPercentage;
        WearPercentage = health.WearPercentage;
        RatingText = $"Reported Capacity Retention: {health.HealthPercentage:F0}% ({health.Rating})";
        CycleCountText = health.CycleCount.HasValue ? $"{health.CycleCount.Value} cycles" : "Sensor Unavailable";
        FullChargeCapacityText = health.FullChargeCapacityMwh.HasValue ? $"{health.FullChargeCapacityMwh.Value:N0} mWh" : "Unavailable";
        DesignCapacityText = health.DesignCapacityMwh.HasValue ? $"{health.DesignCapacityMwh.Value:N0} mWh" : "Unavailable";
        ChemistryText = (reading.Chemistry.HasValue && !string.IsNullOrEmpty(reading.Chemistry.Value) && reading.Chemistry.Value != "Unknown")
            ? reading.Chemistry.Value
            : "Lithium-Ion / ACPI Smart Battery";

        if (health.FullChargeCapacityMwh.HasValue && health.DesignCapacityMwh.HasValue && health.DesignCapacityMwh.Value > 0)
        {
            var ratio = (health.FullChargeCapacityMwh.Value / health.DesignCapacityMwh.Value) * 100.0;
            ReportedRatioText = $"{ratio:F1}%";

            if (Math.Abs(health.FullChargeCapacityMwh.Value - health.DesignCapacityMwh.Value) < 1.0)
            {
                HasFirmwareEqualityWarning = true;
                FirmwareEqualityNotice = "Notice: Design Capacity and Full Charge Capacity are reported identically by battery firmware. On newer or freshly initialized packs, the embedded controller may report design capacity until multiple full discharge/recharge cycles have completed.";
            }
            else
            {
                HasFirmwareEqualityWarning = false;
                FirmwareEqualityNotice = "";
            }
        }
        else
        {
            ReportedRatioText = "Unavailable";
            HasFirmwareEqualityWarning = false;
            FirmwareEqualityNotice = "";
        }

        ProvenanceNotice = reading.IsSimulated
            ? "Values are simulated using HP Victus standard 70 Wh profile (Demo Mode)."
            : "Values are reported directly from physical battery controller.";

        LastUpdatedText = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // Load historical observations
        try
        {
            var history = await _historyRepository.GetHealthObservationsAsync(20);
            HistoricalObservations.Clear();
            foreach (var obs in history)
            {
                HistoricalObservations.Add(obs);
            }

            if (HistoricalObservations.Count >= 2)
            {
                var oldest = HistoricalObservations[^1];
                var newest = HistoricalObservations[0];
                var delta = newest.FullChargeCapacityMwh - oldest.FullChargeCapacityMwh;
                if (delta < -100)
                {
                    HealthTrendText = $"Declining ({delta:N0} mWh change over observed period)";
                }
                else if (delta > 100)
                {
                    HealthTrendText = $"Recalibrated (+{delta:N0} mWh change over observed period)";
                }
                else
                {
                    HealthTrendText = "Stable within normal measurement tolerances";
                }
            }
            else
            {
                HealthTrendText = "Baseline recorded. Trend requires further charging cycles.";
            }
        }
        catch
        {
            // Non-fatal
        }
    }
}
