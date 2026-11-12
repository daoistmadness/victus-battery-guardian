using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Models;
using VictusGuardian.Storage.Repositories;

namespace VictusGuardian.App.ViewModels;

public partial class ChargingViewModel : ViewModelBase
{
    private readonly IBatteryTelemetryProvider _telemetryProvider;
    private readonly IBatteryHistoryRepository _historyRepository;

    [ObservableProperty]
    private string _acLineStatus = "Disconnected (Battery)";

    [ObservableProperty]
    private string _chargingState = "Discharging";

    [ObservableProperty]
    private double _batteryPercentage = 0.0;

    [ObservableProperty]
    private string _chargeDischargeRate = "Unavailable";

    [ObservableProperty]
    private string _sessionDurationText = "N/A";

    [ObservableProperty]
    private string _energyGainedText = "N/A";

    [ObservableProperty]
    private string _targetChargeMode = "Longevity Advisory (80% Target)";

    [ObservableProperty]
    private string _advisoryMessage = "Battery status normal.";

    public ObservableCollection<ChargingSessionRecord> RecentSessions { get; } = new();

    public ChargingViewModel(
        IBatteryTelemetryProvider telemetryProvider,
        IBatteryHistoryRepository historyRepository)
    {
        _telemetryProvider = telemetryProvider;
        _historyRepository = historyRepository;
    }

    [RelayCommand]
    public async Task RefreshChargingAsync()
    {
        var reading = await _telemetryProvider.GetReadingAsync();

        AcLineStatus = reading.IsAcConnected ? "Connected (AC Mains)" : "Disconnected (Battery)";
        ChargingState = reading.ChargingState.HasValue ? reading.ChargingState.Value.ToString() : "Unknown";
        BatteryPercentage = reading.BatteryPercentage.HasValue ? reading.BatteryPercentage.Value : 0.0;
        ChargeDischargeRate = reading.RateInWatts.HasValue
            ? $"{(reading.ChargeRateMilliwatts.Value < 0 ? "-" : "+")}{reading.RateInWatts.Value:F1} W"
            : "Unavailable (Rate sensor not exposed by hardware)";

        if (reading.IsAcConnected)
        {
            if (reading.ChargingState.HasValue && reading.ChargingState.Value == BatteryChargingState.Charging)
            {
                AdvisoryMessage = "Charging in progress. Maintaining battery between 20% and 80% reduces electrode stress and preserves longevity.";
            }
            else if (reading.ChargingState.HasValue && reading.ChargingState.Value == BatteryChargingState.NotCharging)
            {
                AdvisoryMessage = "AC connected but battery is not actively charging. This occurs when battery micro-controller or thermal protection temporarily halts charging, or battery is near target. (Note: Victus Guardian does not control BIOS charge limits).";
            }
            else if (reading.ChargingState.HasValue && reading.ChargingState.Value == BatteryChargingState.Full)
            {
                AdvisoryMessage = "Battery is fully charged. Disconnecting the AC adapter when mobile is recommended to minimize high-voltage cell dwell time.";
            }
            else
            {
                AdvisoryMessage = "AC adapter is connected. Power is being supplied from mains.";
            }
        }
        else
        {
            AdvisoryMessage = "Operating on battery power. Guardian alerts will trigger when battery reaches low (20%) or critical (10%) thresholds.";
        }

        // Active session & recent sessions
        try
        {
            var activeSession = await _historyRepository.GetActiveChargingSessionAsync();
            if (activeSession != null)
            {
                var duration = DateTimeOffset.UtcNow - activeSession.StartTime;
                SessionDurationText = $"{(int)duration.TotalMinutes} min (active)";
                if (reading.RemainingCapacityMwh.HasValue && activeSession.StartEnergyMwh.HasValue)
                {
                    var gained = Math.Max(0.0, reading.RemainingCapacityMwh.Value - activeSession.StartEnergyMwh.Value);
                    EnergyGainedText = $"+{gained:N0} mWh gained";
                }
                else if (reading.BatteryPercentage.HasValue)
                {
                    var pctGained = Math.Max(0.0, reading.BatteryPercentage.Value - activeSession.StartPercentage);
                    EnergyGainedText = $"+{pctGained:F0}% gained";
                }
                else
                {
                    EnergyGainedText = "Recording session...";
                }
            }
            else
            {
                SessionDurationText = "No active charging session";
                EnergyGainedText = "N/A";
            }

            var sessions = await _historyRepository.GetRecentChargingSessionsAsync(15);
            RecentSessions.Clear();
            foreach (var session in sessions)
            {
                RecentSessions.Add(session);
            }
        }
        catch
        {
            // Non-fatal
        }
    }
}
