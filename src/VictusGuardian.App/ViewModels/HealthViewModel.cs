using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;

namespace VictusGuardian.App.ViewModels;

public partial class HealthViewModel : ViewModelBase
{
    private readonly IBatteryTelemetryProvider _telemetryProvider;
    private readonly IBatteryHealthCalculator _healthCalculator;

    [ObservableProperty]
    private double _healthPercentage = 95.0;

    [ObservableProperty]
    private double _wearPercentage = 5.0;

    [ObservableProperty]
    private string _ratingText = "Excellent";

    [ObservableProperty]
    private string _cycleCountText = "42 cycles";

    [ObservableProperty]
    private string _fullChargeCapacityText = "66,500 mWh";

    [ObservableProperty]
    private string _designCapacityText = "70,070 mWh";

    [ObservableProperty]
    private string _provenanceNotice = "Values are simulated using HP Victus standard 70 Wh battery specs.";

    public HealthViewModel(IBatteryTelemetryProvider telemetryProvider, IBatteryHealthCalculator healthCalculator)
    {
        _telemetryProvider = telemetryProvider;
        _healthCalculator = healthCalculator;
    }

    [RelayCommand]
    public async Task RefreshHealthAsync()
    {
        var reading = await _telemetryProvider.GetReadingAsync();
        var health = _healthCalculator.Calculate(reading);

        HealthPercentage = health.HealthPercentage;
        WearPercentage = health.WearPercentage;
        RatingText = health.Rating.ToString();
        CycleCountText = health.CycleCount.HasValue ? $"{health.CycleCount.Value} cycles" : "Sensor Unavailable";
        FullChargeCapacityText = health.FullChargeCapacityMwh.HasValue ? $"{health.FullChargeCapacityMwh.Value:N0} mWh" : "Unavailable";
        DesignCapacityText = health.DesignCapacityMwh.HasValue ? $"{health.DesignCapacityMwh.Value:N0} mWh" : "Unavailable";
        ProvenanceNotice = reading.IsSimulated
            ? "Values are simulated using HP Victus standard 70 Wh profile (Demo Mode)."
            : "Values are reported directly from physical battery controller.";
    }
}
