using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VictusGuardian.Core.Configuration;

public class AppSettings
{
    public int PollingIntervalSeconds { get; set; } = 5;
    public double LowBatteryWarningThreshold { get; set; } = 20.0;
    public double CriticalBatteryThreshold { get; set; } = 10.0;
    public double HighChargeReminderThreshold { get; set; } = 80.0;
    public bool EnableNotifications { get; set; } = true;
    public bool EnableDemoMode { get; set; } = false;
    public int HistoryRetentionDays { get; set; } = 30;
    public bool StartMinimized { get; set; } = false;
}

public interface ISettingsService
{
    AppSettings CurrentSettings { get; }
    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public class JsonFileSettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private AppSettings _currentSettings = new();
    private readonly object _lock = new();

    public AppSettings CurrentSettings
    {
        get
        {
            lock (_lock)
            {
                return _currentSettings;
            }
        }
    }

    public JsonFileSettingsService(string? customPath = null)
    {
        if (string.IsNullOrWhiteSpace(customPath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "VictusGuardian");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            _settingsFilePath = Path.Combine(folder, "settings.json");
        }
        else
        {
            _settingsFilePath = customPath;
        }
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            var defaults = new AppSettings();
            await SaveSettingsAsync(defaults, cancellationToken);
            return defaults;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded != null)
            {
                lock (_lock)
                {
                    _currentSettings = loaded;
                }
                return loaded;
            }
        }
        catch
        {
            // On corrupted file, fallback to defaults
        }

        return CurrentSettings;
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsFilePath, json, cancellationToken);

        lock (_lock)
        {
            _currentSettings = settings;
        }
    }
}
