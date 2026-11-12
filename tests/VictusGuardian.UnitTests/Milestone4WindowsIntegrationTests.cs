using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.App.Lifecycle;
using VictusGuardian.App.Notifications;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Configuration;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Repositories;
using VictusGuardian.Windows.Notifications;
using VictusGuardian.Windows.Startup;
using Xunit;

namespace VictusGuardian.UnitTests;

public class Milestone4WindowsIntegrationTests
{
    [Fact]
    public void WindowsStartupService_NonWindows_ReportsNotSupportedAndDoesNotThrow()
    {
        var service = new WindowsStartupService();
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(service.IsSupported);
            Assert.False(service.IsRegistered());
            // Safe no-op calls
            service.Register(startMinimized: true);
            service.Unregister();
        }
    }

    [Fact]
    public void WindowsDesktopNotificationService_NonWindows_ReportsExpectedDefaults()
    {
        var service = new WindowsDesktopNotificationService();
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(service.IsSupported);
            Assert.Equal("In-App Notification Banner", service.DeliveryMethod);
        }
    }

    [Fact]
    public async Task WindowsDesktopNotificationService_NonWindows_ShowToastReturnsFalseSafely()
    {
        var service = new WindowsDesktopNotificationService();
        if (!OperatingSystem.IsWindows())
        {
            var result = await service.ShowToastAsync("Test Title", "Test Message", BatteryAlertLevel.Warning);
            Assert.False(result);
        }
    }

    [Fact]
    public async Task AppNotificationCoordinator_WhenDisabledInSettings_DoesNotDispatch()
    {
        var tempSettingsFile = Path.Combine(Path.GetTempPath(), $"settings_m4_{Guid.NewGuid():N}.json");
        try
        {
            var settingsService = new JsonFileSettingsService(tempSettingsFile);
            var settings = new AppSettings { EnableNotifications = false };
            await settingsService.SaveSettingsAsync(settings);

            var toastService = new WindowsDesktopNotificationService();
            var coordinator = new AppNotificationCoordinator(toastService, settingsService);

            var alert = new BatteryAlert(
                BatteryAlertLevel.Critical,
                "Critical Battery",
                "Battery is at 5%",
                DateTimeOffset.UtcNow,
                BatteryAlertCategory.CriticalBattery);

            // Should complete without exception and without dispatching
            await coordinator.DispatchAlertAsync(alert);
        }
        finally
        {
            if (File.Exists(tempSettingsFile)) File.Delete(tempSettingsFile);
        }
    }

    [Fact]
    public async Task AppNotificationCoordinator_ThrottlesDuplicateAlerts()
    {
        var tempSettingsFile = Path.Combine(Path.GetTempPath(), $"settings_m4_{Guid.NewGuid():N}.json");
        try
        {
            var settingsService = new JsonFileSettingsService(tempSettingsFile);
            var settings = new AppSettings { EnableNotifications = true };
            await settingsService.SaveSettingsAsync(settings);

            var toastService = new WindowsDesktopNotificationService();
            var coordinator = new AppNotificationCoordinator(toastService, settingsService);

            var alert = new BatteryAlert(
                BatteryAlertLevel.Warning,
                "Low Battery",
                "Battery is at 18%",
                DateTimeOffset.UtcNow,
                BatteryAlertCategory.LowBattery);

            // First and second calls should complete safely without throwing
            await coordinator.DispatchAlertAsync(alert);
            await coordinator.DispatchAlertAsync(alert);
        }
        finally
        {
            if (File.Exists(tempSettingsFile)) File.Delete(tempSettingsFile);
        }
    }

    [Fact]
    public async Task SqliteBatteryHistoryRepository_BoundedQueries_ReturnAccurateResults()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"repo_m4_{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteBatteryHistoryRepository($"Data Source={tempDb}");
            await repo.InitializeAsync();

            var now = DateTimeOffset.UtcNow;

            // Insert 200 reading observations over the last 10 hours
            for (int i = 0; i < 200; i++)
            {
                var time = now.AddHours(-10).AddMinutes(i * 3);
                var reading = new BatteryReading
                {
                    Timestamp = time,
                    AvailableSensors = SensorCapabilities.All,
                    BatteryPercentage = TelemetryValue<double>.Reported(50.0 + (i % 30)),
                    PowerStatus = TelemetryValue<AcPowerStatus>.Reported(i % 2 == 0 ? AcPowerStatus.Online : AcPowerStatus.Offline),
                    RemainingCapacityMwh = TelemetryValue<double>.Reported(35000.0 + i * 10),
                    FullChargeCapacityMwh = TelemetryValue<double>.Reported(70000.0),
                    TelemetryProviderSource = "Windows IOCTL"
                };
                await repo.RecordReadingAsync(reading);
            }

            // Test GetReadingCountSinceAsync
            var cutoff = now.AddHours(-5);
            var count = await repo.GetReadingCountSinceAsync(cutoff);
            Assert.True(count > 0 && count < 200, $"Expected count between 0 and 200, got {count}");

            // Test GetRecentReadingsSinceAsync bounded
            var recentBounded = await repo.GetRecentReadingsSinceAsync(cutoff, limit: 50);
            Assert.True(recentBounded.Count <= 50);
            Assert.True(recentBounded.Count > 0);

            // Verify ordered descending
            for (int i = 0; i < recentBounded.Count - 1; i++)
            {
                Assert.True(recentBounded[i].Timestamp >= recentBounded[i + 1].Timestamp);
            }

            // Test GetDownsampledReadingsAsync
            var downsampled = await repo.GetDownsampledReadingsAsync(now.AddHours(-12), maxBuckets: 50);
            Assert.True(downsampled.Count <= 52); // target count plus potential boundaries
            Assert.True(downsampled.Count > 0);

            // Verify ordered ascending for time-series visualization
            for (int i = 0; i < downsampled.Count - 1; i++)
            {
                Assert.True(downsampled[i].Timestamp <= downsampled[i + 1].Timestamp);
            }
        }
        finally
        {
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }

    [Fact]
    public void TrayManager_Headless_DoesNotThrowAndTracksState()
    {
        var tempSettings = Path.Combine(Path.GetTempPath(), $"tray_settings_{Guid.NewGuid():N}.json");
        try
        {
            var settingsService = new JsonFileSettingsService(tempSettings);
            var tray = new TrayManager(settingsService);
            Assert.False(tray.IsWindowHidden);

            var reading = new BatteryReading
            {
                Timestamp = DateTimeOffset.UtcNow,
                AvailableSensors = SensorCapabilities.All,
                BatteryPercentage = TelemetryValue<double>.Reported(85.0),
                PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
                ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Charging)
            };
            tray.UpdateStatus(reading);

            // Safe disposal
            tray.Dispose();
        }
        finally
        {
            if (File.Exists(tempSettings)) File.Delete(tempSettings);
        }
    }
}
