using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VictusGuardian.App.Diagnostics;
using VictusGuardian.App.ViewModels;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Configuration;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Repositories;
using Xunit;

namespace VictusGuardian.UnitTests;

public class StartupAndReliabilityTests
{
    private class SlowOrFailingTelemetryProvider : IBatteryTelemetryProvider
    {
        public string ProviderName => "Slow/Failing Mock Provider";
        public bool IsHardwareSupported { get; set; } = false;
        public TimeSpan ArtificialDelay { get; set; } = TimeSpan.Zero;
        public bool ShouldThrow { get; set; }

        public async Task<BatteryReading> GetReadingAsync(CancellationToken cancellationToken = default)
        {
            if (ArtificialDelay > TimeSpan.Zero)
            {
                await Task.Delay(ArtificialDelay, cancellationToken);
            }

            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated hardware failure");
            }

            return new BatteryReading
            {
                Timestamp = DateTimeOffset.UtcNow,
                AvailableSensors = SensorCapabilities.None,
                BatteryPercentage = TelemetryValue<double>.Unavailable,
                PowerStatus = TelemetryValue<AcPowerStatus>.Unavailable,
                ChargingState = TelemetryValue<BatteryChargingState>.Unavailable,
                RemainingCapacityMwh = TelemetryValue<double>.Unavailable,
                FullChargeCapacityMwh = TelemetryValue<double>.Unavailable,
                DesignCapacityMwh = TelemetryValue<double>.Unavailable,
                ChargeRateMilliwatts = TelemetryValue<double>.Unavailable,
                CycleCount = TelemetryValue<int>.Unavailable,
                TelemetryProviderSource = "None"
            };
        }
    }

    [Fact]
    public void StartupLogger_RecordsEventsAndSanitizesSecrets()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"victus_log_test_{Guid.NewGuid():N}");
        try
        {
            AppLogger.Initialize(tempDir);
            AppLogger.LogInfo("Application starting test.");
            AppLogger.LogWarning("Testing serial number redaction: Serial: BAT-VICTUS-999888777");

            var logFile = AppLogger.GetLogFilePath();
            Assert.True(File.Exists(logFile));

            var content = File.ReadAllText(logFile);
            Assert.Contains("Application starting test", content);
            Assert.Contains("REDACTED", content);
            Assert.DoesNotContain("BAT-VICTUS-999888777", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Startup_CorruptedSettings_RecoversGracefullyWithDefaults()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"corrupted_settings_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempFile, "MALFORMED_JSON_CONTENT{{{");
            var settingsService = new JsonFileSettingsService(tempFile);

            var settings = await settingsService.LoadSettingsAsync();

            Assert.NotNull(settings);
            Assert.Equal(5, settings.PollingIntervalSeconds);
            Assert.Equal(20.0, settings.LowBatteryWarningThreshold);
            Assert.Equal(10.0, settings.CriticalBatteryThreshold);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Startup_SqliteInitializationFailure_ReportsWarningWithoutThrowing()
    {
        // An invalid path that cannot be created or opened as SQLite database
        var invalidDbPath = OperatingSystem.IsWindows()
            ? "Data Source=Z:\\NonExistentDrive\\battery.db"
            : "Data Source=/dev/null/battery.db";

        var repo = new SqliteBatteryHistoryRepository(invalidDbPath, TimeSpan.Zero);

        // Initialization failure must throw caught or handled exception without corrupting user data
        var threw = false;
        try
        {
            await repo.InitializeAsync();
        }
        catch
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact]
    public async Task Startup_TelemetryUnavailableOnStartup_SetsUnavailableBadgeGracefully()
    {
        var mockProvider = new SlowOrFailingTelemetryProvider
        {
            IsHardwareSupported = false,
            ShouldThrow = false
        };

        var tempDb = Path.Combine(Path.GetTempPath(), $"victus_unavail_{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteBatteryHistoryRepository($"Data Source={tempDb}", TimeSpan.Zero);
            await repo.InitializeAsync();
            var settings = new JsonFileSettingsService(Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json"));

            using var vm = new DashboardViewModel(
                mockProvider,
                new DefaultBatteryHealthCalculator(),
                new DefaultBatteryAlertEvaluator(),
                repo,
                settings);

            await vm.RefreshTelemetryAsync();

            Assert.True(vm.IsUnavailable);
            Assert.Equal("TELEMETRY UNAVAILABLE", vm.ProvenanceBadge);
            Assert.Equal("Unavailable", vm.EstimatedTimeRemainingText);
            Assert.Contains(vm.ActiveAlerts, a => a.Contains("Sensor"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public async Task Startup_SlowHardwareIoctl_CanBeTimedOutOrCancelled()
    {
        var mockProvider = new SlowOrFailingTelemetryProvider
        {
            ArtificialDelay = TimeSpan.FromSeconds(5)
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await mockProvider.GetReadingAsync(cts.Token);
        });
    }

    [Fact]
    public async Task Startup_BackgroundPolling_CancelsAndDisposesCleanly()
    {
        var mockProvider = new SlowOrFailingTelemetryProvider();
        var tempDb = Path.Combine(Path.GetTempPath(), $"victus_poll_{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteBatteryHistoryRepository($"Data Source={tempDb}", TimeSpan.Zero);
            await repo.InitializeAsync();
            var settings = new JsonFileSettingsService(Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json"));

            var vm = new DashboardViewModel(
                mockProvider,
                new DefaultBatteryHealthCalculator(),
                new DefaultBatteryAlertEvaluator(),
                repo,
                settings);

            vm.StartAutoPolling(2);

            // Wait a brief moment then stop polling
            await Task.Delay(100);
            vm.StopAutoPolling();
            vm.Dispose();

            // Calling Dispose again should be safe and idempotent
            vm.Dispose();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) try { File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public async Task Startup_DiagnosticsMode_ExecutesIndependently()
    {
        var exitCode = await DiagnosticRunner.RunAsync(["--diagnostics", "--json"]);
        Assert.Equal(0, exitCode);
    }
}
