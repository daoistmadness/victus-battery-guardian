using System;
using System.IO;
using System.Threading.Tasks;
using VictusGuardian.Core.Configuration;
using Xunit;

namespace VictusGuardian.UnitTests;

public class SettingsServiceTests
{
    [Fact]
    public async Task LoadSettingsAsync_WhenFileDoesNotExist_ReturnsAndSavesDefaults()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new JsonFileSettingsService(tempFile);
            var loaded = await service.LoadSettingsAsync();

            Assert.NotNull(loaded);
            Assert.Equal(30, loaded.PollingIntervalSeconds);
            Assert.Equal(20.0, loaded.LowBatteryWarningThreshold);
            Assert.Equal(10.0, loaded.CriticalBatteryThreshold);
            Assert.Equal(80.0, loaded.HighChargeReminderThreshold);
            Assert.True(loaded.EnableNotifications);
            Assert.False(loaded.EnableDemoMode);
            Assert.False(loaded.MinimizeToTray);
            Assert.False(loaded.CloseToTray);
            Assert.True(loaded.ShowTrayIcon);
            Assert.False(loaded.StartAtLogin);
            Assert.False(loaded.StartMinimized);
            Assert.True(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsModifiedValues()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new JsonFileSettingsService(tempFile);
            var newSettings = new AppSettings
            {
                PollingIntervalSeconds = 15,
                LowBatteryWarningThreshold = 25.0,
                CriticalBatteryThreshold = 8.0,
                HighChargeReminderThreshold = 85.0,
                EnableNotifications = false,
                EnableDemoMode = true,
                HistoryRetentionDays = 14
            };

            await service.SaveSettingsAsync(newSettings);

            // Read with fresh instance
            var service2 = new JsonFileSettingsService(tempFile);
            var loaded = await service2.LoadSettingsAsync();

            Assert.Equal(15, loaded.PollingIntervalSeconds);
            Assert.Equal(25.0, loaded.LowBatteryWarningThreshold);
            Assert.Equal(8.0, loaded.CriticalBatteryThreshold);
            Assert.Equal(85.0, loaded.HighChargeReminderThreshold);
            Assert.False(loaded.EnableNotifications);
            Assert.True(loaded.EnableDemoMode);
            Assert.Equal(14, loaded.HistoryRetentionDays);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LoadSettingsAsync_WhenFileIsCorrupted_FallsBackToDefaultsWithoutCrashing()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempFile, "{ INVALID JSON DATA !!!");
            var service = new JsonFileSettingsService(tempFile);
            var loaded = await service.LoadSettingsAsync();

            Assert.NotNull(loaded);
            Assert.Equal(30, loaded.PollingIntervalSeconds);
            Assert.False(loaded.MinimizeToTray);
            Assert.False(loaded.CloseToTray);
            Assert.True(loaded.ShowTrayIcon);
            Assert.False(loaded.StartAtLogin);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
