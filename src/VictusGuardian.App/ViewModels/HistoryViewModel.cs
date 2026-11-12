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
    private string _statusMessage = "Loading history...";

    [ObservableProperty]
    private int _recordCount = 0;

    public ObservableCollection<BatteryReadingRecord> RecentRecords { get; } = new();

    public HistoryViewModel(IBatteryHistoryRepository historyRepository)
    {
        _historyRepository = historyRepository;
    }

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        try
        {
            var records = await _historyRepository.GetRecentReadingsAsync(50);
            RecentRecords.Clear();
            foreach (var record in records)
            {
                RecentRecords.Add(record);
            }

            RecordCount = RecentRecords.Count;
            StatusMessage = RecordCount > 0
                ? $"{RecordCount} recorded readings in local SQLite database"
                : "No telemetry records in database yet. Telemetry will be recorded as readings arrive.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load history: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ClearHistoryAsync()
    {
        try
        {
            await _historyRepository.PurgeOldReadingsAsync(DateTimeOffset.UtcNow.AddMinutes(1));
            RecentRecords.Clear();
            RecordCount = 0;
            StatusMessage = "History cleared.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to clear history: {ex.Message}";
        }
    }
}
