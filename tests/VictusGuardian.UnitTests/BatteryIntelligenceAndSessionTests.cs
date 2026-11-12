using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Models;
using VictusGuardian.Storage.Repositories;
using Xunit;

namespace VictusGuardian.UnitTests;

public class BatteryIntelligenceAndSessionTests
{
    [Fact]
    public async Task ChargingSession_Lifecycle_CalculatesDurationAndEnergyAccurately()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"victus_session_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";

        try
        {
            var repo = new SqliteBatteryHistoryRepository(connStr, TimeSpan.Zero);
            await repo.InitializeAsync();

            var startTime = DateTimeOffset.UtcNow.AddMinutes(-45);
            var sessionId = await repo.StartChargingSessionAsync(new ChargingSessionRecord
            {
                StartTime = startTime,
                StartPercentage = 25.0,
                StartEnergyMwh = 17500.0,
                ProviderSource = "Windows IOCTL (Direct Device Interface)",
                BatterySerialNumber = "BAT-VICTUS-001",
                IsCompleted = false
            });

            Assert.True(sessionId > 0);

            var active = await repo.GetActiveChargingSessionAsync();
            Assert.NotNull(active);
            Assert.Equal(25.0, active.StartPercentage);
            Assert.False(active.IsCompleted);

            var endTime = startTime.AddMinutes(45);
            var completed = await repo.CompleteChargingSessionAsync(sessionId, endTime, 80.0, 56000.0);
            Assert.True(completed);

            var activeAfter = await repo.GetActiveChargingSessionAsync();
            Assert.Null(activeAfter);

            var sessions = await repo.GetRecentChargingSessionsAsync(10);
            Assert.Single(sessions);
            var session = sessions[0];
            Assert.True(session.IsCompleted);
            Assert.Equal(25.0, session.StartPercentage);
            Assert.Equal(80.0, session.EndPercentage);
            Assert.Equal(45.0, session.DurationMinutes!.Value, 1);
            Assert.Equal(38500.0, session.EnergyGainedMwh!.Value, 1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task RecordHealthObservation_PreventsIdenticalDuplicateObservations()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"victus_health_hist_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";

        try
        {
            var repo = new SqliteBatteryHistoryRepository(connStr, TimeSpan.Zero);
            await repo.InitializeAsync();

            var obs1 = new BatteryHealthRecord
            {
                Timestamp = DateTimeOffset.UtcNow.AddHours(-1),
                FullChargeCapacityMwh = 68237.0,
                DesignCapacityMwh = 68237.0,
                CycleCount = 2,
                BatterySerialNumber = "SN9999",
                BatteryName = "Primary",
                Chemistry = "LION"
            };

            var recorded1 = await repo.RecordHealthObservationAsync(obs1);
            Assert.True(recorded1);

            // Identical observation should be throttled/deduplicated
            var obsDuplicate = obs1 with { Timestamp = DateTimeOffset.UtcNow };
            var recordedDuplicate = await repo.RecordHealthObservationAsync(obsDuplicate);
            Assert.False(recordedDuplicate);

            // Changed observation (cycle count incremented) should be recorded
            var obsChanged = obs1 with { Timestamp = DateTimeOffset.UtcNow, CycleCount = 3 };
            var recordedChanged = await repo.RecordHealthObservationAsync(obsChanged);
            Assert.True(recordedChanged);

            var history = await repo.GetHealthObservationsAsync(10);
            Assert.Equal(2, history.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task GetUsageStatistics_ComputesAggregatesAndEstimatedRuntime()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"victus_stats_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";

        try
        {
            var repo = new SqliteBatteryHistoryRepository(connStr, TimeSpan.Zero);
            await repo.InitializeAsync();

            var t0 = DateTimeOffset.UtcNow.AddHours(-2);

            // Insert simulated discharge readings
            for (int i = 0; i < 5; i++)
            {
                var reading = new BatteryReading
                {
                    Timestamp = t0.AddMinutes(i * 15),
                    AvailableSensors = SensorCapabilities.All,
                    BatteryPercentage = TelemetryValue<double>.Reported(80.0 - (i * 5)),
                    PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
                    ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging),
                    RemainingCapacityMwh = TelemetryValue<double>.Reported(50000.0 - (i * 3500.0)),
                    FullChargeCapacityMwh = TelemetryValue<double>.Reported(68000.0),
                    DesignCapacityMwh = TelemetryValue<double>.Reported(70000.0),
                    ChargeRateMilliwatts = TelemetryValue<double>.Reported(-14000.0), // -14 W
                    CycleCount = TelemetryValue<int>.Reported(5),
                    VoltageMillivolts = TelemetryValue<double>.Reported(14800.0),
                    TelemetryProviderSource = "Windows IOCTL"
                };
                await repo.RecordReadingAsync(reading);
            }

            var stats = await repo.GetUsageStatisticsAsync(TimeSpan.FromHours(24));
            Assert.NotNull(stats);
            Assert.NotNull(stats.AverageDischargeRateWatts);
            Assert.Equal(14.0, stats.AverageDischargeRateWatts.Value, 1);
            Assert.NotNull(stats.EstimatedRuntimeFromHistory);
            Assert.True(stats.EstimatedRuntimeFromHistory.Value.TotalHours > 1.0);
            Assert.True(stats.TotalBatteryTimeHours > 0);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
        }
    }
}
