using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Models;

namespace VictusGuardian.App.ViewModels;

public partial class ChargingViewModel : ViewModelBase
{
    private readonly IBatteryTelemetryProvider _telemetryProvider;

    [ObservableProperty]
    private string _acLineStatus = "Disconnected (Battery)";

    [ObservableProperty]
    private string _chargingState = "Discharging";

    [ObservableProperty]
    private string _chargeDischargeRate = "18.5 W";

    [ObservableProperty]
    private string _estimatedTime = "2h 49m";

    [ObservableProperty]
    private string _targetChargeMode = "Longevity Target (80%)";

    [ObservableProperty]
    private string _advisoryMessage = "Discharging on battery power. Modern lithium batteries perform best when kept between 20% and 80%.";

    public ChargingViewModel(IBatteryTelemetryProvider telemetryProvider)
    {
        _telemetryProvider = telemetryProvider;
    }

    [RelayCommand]
    public async Task RefreshChargingAsync()
    {
        var reading = await _telemetryProvider.GetReadingAsync();

        AcLineStatus = reading.IsAcConnected ? "Connected (AC Mains)" : "Disconnected (Battery)";
        ChargingState = reading.ChargingState.HasValue ? reading.ChargingState.Value.ToString() : "Unknown";
        ChargeDischargeRate = reading.RateInWatts.HasValue ? $"{reading.RateInWatts.Value:F1} W" : "Unavailable";
        EstimatedTime = reading.EstimatedTimeRemaining.HasValue
            ? $"{(int)reading.EstimatedTimeRemaining.Value.TotalHours}h {reading.EstimatedTimeRemaining.Value.Minutes}m"
            : (reading.ChargingState.Value == BatteryChargingState.Full ? "Full" : "Calculating...");

        if (reading.IsAcConnected)
        {
            AdvisoryMessage = "AC adapter is connected. Real charging threshold enforcement will be integrated via platform API in Milestone 2.";
        }
        else
        {
            AdvisoryMessage = "Running on battery. Low-power alerts will trigger when capacity reaches 20% and 10%.";
        }
    }
}
