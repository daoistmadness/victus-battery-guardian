using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VictusGuardian.Storage.Models;
using VictusGuardian.Storage.Repositories;

namespace VictusGuardian.App.ViewModels;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly IBatteryHistoryRepository _historyRepository;

    [ObservableProperty]
    private string _selectedTimeRange = "24 Hours";

    [ObservableProperty]
    private string _statusMessage = "Loading history...";

    [ObservableProperty]
    private int _recordCount = 0;

    [ObservableProperty]
    private string _averageDischargeRateText = "Calculating...";

    [ObservableProperty]
    private string _estimatedRuntimeText = "N/A";

    [ObservableProperty]
    private string _acTimeText = "0 h";

    [ObservableProperty]
    private string _batteryTimeText = "0 h";

    [ObservableProperty]
    private string _highChargeTimeText = "N/A";

    [ObservableProperty]
    private string _totalSessionsText = "0";

    public ObservableCollection<BatteryReadingRecord> RecentRecords { get; } = new();

    public HistoryViewModel(IBatteryHistoryRepository historyRepository)
    {
        _historyRepository = historyRepository;
    }

    [RelayCommand]
    public async Task SetTimeRangeAsync(string range)
    {
        SelectedTimeRange = range;
        await LoadHistoryAsync();
    }

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        try
        {
            var period = SelectedTimeRange switch
            {
                "7 Days" => TimeSpan.FromDays(7),
                "30 Days" => TimeSpan.FromDays(30),
                _ => TimeSpan.FromHours(24)
            };

            var since = DateTimeOffset.UtcNow - period;
            var records = await _historyRepository.GetReadingsSinceAsync(since);

            RecentRecords.Clear();
            // Show newest first in UI
            for (int i = records.Count - 1; i >= 0 && RecentRecords.Count < 100; i--)
            {
                RecentRecords.Add(records[i]);
            }

            RecordCount = records.Count;

            // Load statistics
            var stats = await _historyRepository.GetUsageStatisticsAsync(period);
            AverageDischargeRateText = stats.AverageDischargeRateWatts.HasValue
                ? $"{stats.AverageDischargeRateWatts.Value:F1} W"
                : "Insufficient discharge data";

            EstimatedRuntimeText = stats.EstimatedRuntimeFromHistory.HasValue
                ? $"~{(int)stats.EstimatedRuntimeFromHistory.Value.TotalHours}h {stats.EstimatedRuntimeFromHistory.Value.Minutes}m"
                : "N/A";

            AcTimeText = stats.TotalAcTimeHours.HasValue ? $"{stats.TotalAcTimeHours.Value:F1} h" : "0 h";
            BatteryTimeText = stats.TotalBatteryTimeHours.HasValue ? $"{stats.TotalBatteryTimeHours.Value:F1} h" : "0 h";
            HighChargeTimeText = stats.PercentTimeAtHighCharge.HasValue ? $"{stats.PercentTimeAtHighCharge.Value:F0}%" : "0%";
            TotalSessionsText = stats.TotalChargingSessions.ToString();

            StatusMessage = RecordCount > 0
                ? $"{RecordCount} persisted telemetry records in SQLite WAL database ({SelectedTimeRange})"
                : $"No records stored within {SelectedTimeRange}. Records will accumulate during operation.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load history: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task PurgeOldDataAsync()
    {
        try
        {
            var purged = await _historyRepository.PurgeOldReadingsAsync(DateTimeOffset.UtcNow.AddMinutes(1));
            RecentRecords.Clear();
            RecordCount = 0;
            StatusMessage = $"Purged {purged} records from SQLite database.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to purge history: {ex.Message}";
        }
    }
}
