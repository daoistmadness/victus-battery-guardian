using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Models;
using VictusGuardian.Storage.Repositories;
using Xunit;

namespace VictusGuardian.UnitTests;

public class SqliteRepositoryTelemetryMigrationTests
{
    [Fact]
    public async Task InitializeAsync_MigratesV1DatabaseToV3_PreservesReadingsAndAddsColumnsAndTables()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"victus_test_mig_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";

        try
        {
            // 1. Manually create V1 database
            await using (var conn = new SqliteConnection(connStr))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE BatteryTelemetry (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT NOT NULL,
                        Percentage REAL NOT NULL,
                        IsAcConnected INTEGER NOT NULL,
                        ChargingState TEXT NOT NULL,
                        RemainingCapacityMwh REAL,
                        FullChargeCapacityMwh REAL,
                        DesignCapacityMwh REAL,
                        ChargeRateMilliwatts REAL,
                        CycleCount INTEGER,
                        IsSimulated INTEGER NOT NULL
                    );
                    PRAGMA user_version = 1;
                    INSERT INTO BatteryTelemetry (
                        Timestamp, Percentage, IsAcConnected, ChargingState,
                        RemainingCapacityMwh, FullChargeCapacityMwh, DesignCapacityMwh,
                        ChargeRateMilliwatts, CycleCount, IsSimulated
                    ) VALUES (
                        '2026-11-12T00:00:00Z', 80.0, 1, 'Charging',
                        55000.0, 66000.0, 70000.0,
                        30000.0, 25, 0
                    );
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. Run InitializeAsync with the new repository
            var repo = new SqliteBatteryHistoryRepository(connStr, TimeSpan.Zero);
            await repo.InitializeAsync();

            // Verify user_version upgraded to 3
            await using (var conn = new SqliteConnection(connStr))
            {
                await conn.OpenAsync();
                using var vCmd = conn.CreateCommand();
                vCmd.CommandText = "PRAGMA user_version;";
                var ver = Convert.ToInt32(await vCmd.ExecuteScalarAsync());
                Assert.Equal(3, ver);
            }

            // 3. Read existing record
            var readings = await repo.GetRecentReadingsAsync(10);
            Assert.Single(readings);
            Assert.Equal(80.0, readings[0].Percentage);
            Assert.Null(readings[0].VoltageMillivolts); // Null in legacy record

            // 4. Insert new record with voltage and provider
            var newReading = new BatteryReading
            {
                Timestamp = DateTimeOffset.UtcNow,
                AvailableSensors = SensorCapabilities.All,
                BatteryPercentage = TelemetryValue<double>.Reported(75.5),
                PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
                ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging),
                RemainingCapacityMwh = TelemetryValue<double>.Reported(50000),
                FullChargeCapacityMwh = TelemetryValue<double>.Reported(66000),
                DesignCapacityMwh = TelemetryValue<double>.Reported(70000),
                ChargeRateMilliwatts = TelemetryValue<double>.Reported(-15000),
                CycleCount = TelemetryValue<int>.Reported(26),
                VoltageMillivolts = TelemetryValue<double>.Reported(15250),
                TelemetryProviderSource = "Windows IOCTL (Direct Device Interface)"
            };

            var recorded = await repo.RecordReadingAsync(newReading);
            Assert.True(recorded);

            var updatedReadings = await repo.GetRecentReadingsAsync(10);
            Assert.Equal(2, updatedReadings.Count);
            Assert.Equal(75.5, updatedReadings[0].Percentage);
            Assert.Equal(15250.0, updatedReadings[0].VoltageMillivolts);
            Assert.Equal("Windows IOCTL (Direct Device Interface)", updatedReadings[0].ProviderSource);

            // 5. Test v3 tables exist and work
            var sessionId = await repo.StartChargingSessionAsync(new ChargingSessionRecord
            {
                StartTime = DateTimeOffset.UtcNow.AddMinutes(-30),
                StartPercentage = 50.0,
                StartEnergyMwh = 35000,
                ProviderSource = "Windows IOCTL",
                IsCompleted = false
            });
            Assert.True(sessionId > 0);

            var active = await repo.GetActiveChargingSessionAsync();
            Assert.NotNull(active);
            Assert.Equal(50.0, active.StartPercentage);

            var completed = await repo.CompleteChargingSessionAsync(sessionId, DateTimeOffset.UtcNow, 80.0, 56000);
            Assert.True(completed);

            var sessions = await repo.GetRecentChargingSessionsAsync(10);
            Assert.Single(sessions);
            Assert.True(sessions[0].IsCompleted);
            Assert.Equal(30.0, sessions[0].DurationMinutes!.Value, 1);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_MigratesV2DatabaseToV3_CreatesV3Tables()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"victus_test_mig_v2v3_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={dbPath}";

        try
        {
            await using (var conn = new SqliteConnection(connStr))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE BatteryTelemetry (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT NOT NULL,
                        Percentage REAL NOT NULL,
                        IsAcConnected INTEGER NOT NULL,
                        ChargingState TEXT NOT NULL,
                        RemainingCapacityMwh REAL,
                        FullChargeCapacityMwh REAL,
                        DesignCapacityMwh REAL,
                        ChargeRateMilliwatts REAL,
                        CycleCount INTEGER,
                        IsSimulated INTEGER NOT NULL,
                        VoltageMillivolts REAL,
                        ProviderSource TEXT
                    );
                    PRAGMA user_version = 2;
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            var repo = new SqliteBatteryHistoryRepository(connStr, TimeSpan.Zero);
            await repo.InitializeAsync();

            await using (var conn = new SqliteConnection(connStr))
            {
                await conn.OpenAsync();
                using var vCmd = conn.CreateCommand();
                vCmd.CommandText = "PRAGMA user_version;";
                var ver = Convert.ToInt32(await vCmd.ExecuteScalarAsync());
                Assert.Equal(3, ver);
            }

            var recordedHealth = await repo.RecordHealthObservationAsync(new BatteryHealthRecord
            {
                Timestamp = DateTimeOffset.UtcNow,
                FullChargeCapacityMwh = 68000,
                DesignCapacityMwh = 70000,
                CycleCount = 10,
                BatterySerialNumber = "SN12345",
                BatteryName = "VICTUS_BAT",
                Chemistry = "LION"
            });
            Assert.True(recordedHealth);

            var healthObs = await repo.GetHealthObservationsAsync(10);
            Assert.Single(healthObs);
            Assert.Equal(68000, healthObs[0].FullChargeCapacityMwh);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }
}
