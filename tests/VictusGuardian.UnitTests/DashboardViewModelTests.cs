using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VictusGuardian.App.ViewModels;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Configuration;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Repositories;
using Xunit;

namespace VictusGuardian.UnitTests;

public class DashboardViewModelTests
{
    private class MockTelemetryProvider : IBatteryTelemetryProvider
    {
        public string ProviderName => "Mock Direct Device IOCTL";
        public bool IsHardwareSupported => true;
        public BatteryReading CurrentReading { get; set; } = new() { Timestamp = DateTimeOffset.UtcNow, AvailableSensors = SensorCapabilities.All };

        public Task<BatteryReading> GetReadingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentReading);
    }

    [Fact]
    public async Task RefreshTelemetry_WhenDischargingWithRate_CalculatesEstimatedRuntime()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"victus_vm_test_{Guid.NewGuid():N}.db");
        try
        {
            var mockProvider = new MockTelemetryProvider
            {
                CurrentReading = new BatteryReading
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    AvailableSensors = SensorCapabilities.All,
                    BatteryPercentage = TelemetryValue<double>.Reported(50.0),
                    PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
                    ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging),
                    RemainingCapacityMwh = TelemetryValue<double>.Reported(35000.0),
                    FullChargeCapacityMwh = TelemetryValue<double>.Reported(70000.0),
                    DesignCapacityMwh = TelemetryValue<double>.Reported(70000.0),
                    ChargeRateMilliwatts = TelemetryValue<double>.Reported(-14000.0), // 14 W discharge -> 2.5 hours
                    CycleCount = TelemetryValue<int>.Reported(10),
                    TelemetryProviderSource = "Windows IOCTL (Direct Device Interface)"
                }
            };

            var healthCalc = new DefaultBatteryHealthCalculator();
            var alertEval = new DefaultBatteryAlertEvaluator();
            var repo = new SqliteBatteryHistoryRepository($"Data Source={dbPath}", TimeSpan.Zero);
            await repo.InitializeAsync();
            var settings = new JsonFileSettingsService(Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json"));

            using var vm = new DashboardViewModel(mockProvider, healthCalc, alertEval, repo, settings);
            vm.StopAutoPolling();

            await vm.RefreshTelemetryAsync();

            Assert.Equal(50.0, vm.BatteryPercentage);
            Assert.Equal("Discharging", vm.ChargingStateText);
            Assert.Equal("2h 30m remaining", vm.EstimatedTimeRemainingText);
            Assert.NotEqual("Calculating...", vm.EstimatedTimeRemainingText);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task RefreshTelemetry_WhenChargingWithUnknownRate_ReportsRateUnsupportedInsteadOfCalculating()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"victus_vm_test_{Guid.NewGuid():N}.db");
        try
        {
            var mockProvider = new MockTelemetryProvider
            {
                CurrentReading = new BatteryReading
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    AvailableSensors = SensorCapabilities.Percentage | SensorCapabilities.ChargingState | SensorCapabilities.RemainingCapacity | SensorCapabilities.FullChargeCapacity,
                    BatteryPercentage = TelemetryValue<double>.Reported(33.0),
                    PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
                    ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Charging),
                    RemainingCapacityMwh = TelemetryValue<double>.Reported(22283.0),
                    FullChargeCapacityMwh = TelemetryValue<double>.Reported(68237.0),
                    DesignCapacityMwh = TelemetryValue<double>.Reported(68237.0),
                    ChargeRateMilliwatts = TelemetryValue<double>.Unavailable, // Genuine HP Victus hardware case
                    CycleCount = TelemetryValue<int>.Reported(2),
                    TelemetryProviderSource = "Windows IOCTL (Direct Device Interface)"
                }
            };

            var healthCalc = new DefaultBatteryHealthCalculator();
            var alertEval = new DefaultBatteryAlertEvaluator();
            var repo = new SqliteBatteryHistoryRepository($"Data Source={dbPath}", TimeSpan.Zero);
            await repo.InitializeAsync();
            var settings = new JsonFileSettingsService(Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json"));

            using var vm = new DashboardViewModel(mockProvider, healthCalc, alertEval, repo, settings);
            vm.StopAutoPolling();

            await vm.RefreshTelemetryAsync();

            Assert.Equal(33.0, vm.BatteryPercentage);
            Assert.Equal("Charging", vm.ChargingStateText);
            Assert.Equal("Unavailable (Sensor not exposed by hardware)", vm.ChargeRateText);
            Assert.Equal("Unavailable (Charging rate unsupported)", vm.EstimatedTimeRemainingText);
            Assert.NotEqual("Calculating...", vm.EstimatedTimeRemainingText);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task RefreshTelemetry_WhenFull_DisplaysFullyCharged()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"victus_vm_test_{Guid.NewGuid():N}.db");
        try
        {
            var mockProvider = new MockTelemetryProvider
            {
                CurrentReading = new BatteryReading
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    AvailableSensors = SensorCapabilities.All,
                    BatteryPercentage = TelemetryValue<double>.Reported(100.0),
                    PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
                    ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Full),
                    RemainingCapacityMwh = TelemetryValue<double>.Reported(68237.0),
                    FullChargeCapacityMwh = TelemetryValue<double>.Reported(68237.0),
                    DesignCapacityMwh = TelemetryValue<double>.Reported(68237.0),
                    ChargeRateMilliwatts = TelemetryValue<double>.Reported(0.0),
                    CycleCount = TelemetryValue<int>.Reported(2),
                    TelemetryProviderSource = "Windows IOCTL"
                }
            };

            var healthCalc = new DefaultBatteryHealthCalculator();
            var alertEval = new DefaultBatteryAlertEvaluator();
            var repo = new SqliteBatteryHistoryRepository($"Data Source={dbPath}", TimeSpan.Zero);
            await repo.InitializeAsync();
            var settings = new JsonFileSettingsService(Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json"));

            using var vm = new DashboardViewModel(mockProvider, healthCalc, alertEval, repo, settings);
            vm.StopAutoPolling();

            await vm.RefreshTelemetryAsync();

            Assert.Equal("Fully Charged", vm.EstimatedTimeRemainingText);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }
}
