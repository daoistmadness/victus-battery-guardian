using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Models;

namespace VictusGuardian.Storage.Repositories;

public class SqliteBatteryHistoryRepository : IBatteryHistoryRepository
{
    private readonly string _connectionString;
    private readonly TimeSpan _minIdleWriteInterval;
    private BatteryReadingRecord? _lastRecorded;
    private DateTimeOffset _lastRecordedAt = DateTimeOffset.MinValue;
    private readonly object _throttleLock = new();

    public SqliteBatteryHistoryRepository(string? connectionString = null, TimeSpan? minIdleWriteInterval = null)
    {
        _minIdleWriteInterval = minIdleWriteInterval ?? TimeSpan.FromMinutes(5);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "VictusGuardian");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            var dbPath = Path.Combine(folder, "battery_history.db");
            _connectionString = $"Data Source={dbPath}";
        }
        else
        {
            _connectionString = connectionString;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Configure WAL journal mode and normal synchronous for lightweight, reliable operation
        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);

        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        var currentVersionObj = await versionCmd.ExecuteScalarAsync(cancellationToken);
        var currentVersion = Convert.ToInt32(currentVersionObj);

        if (currentVersion < 1)
        {
            using var tx = connection.BeginTransaction();
            using var createCmd = connection.CreateCommand();
            createCmd.Transaction = tx;
            createCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS BatteryTelemetry (
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

                CREATE INDEX IF NOT EXISTS IX_BatteryTelemetry_Timestamp ON BatteryTelemetry (Timestamp);
                PRAGMA user_version = 1;
            ";
            await createCmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            currentVersion = 1;
        }

        if (currentVersion < 2)
        {
            using var tx = connection.BeginTransaction();
            using var alterCmd = connection.CreateCommand();
            alterCmd.Transaction = tx;
            alterCmd.CommandText = @"
                ALTER TABLE BatteryTelemetry ADD COLUMN VoltageMillivolts REAL;
                ALTER TABLE BatteryTelemetry ADD COLUMN ProviderSource TEXT;
                PRAGMA user_version = 2;
            ";
            await alterCmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
    }

    public async Task<bool> RecordReadingAsync(BatteryReading reading, CancellationToken cancellationToken = default)
    {
        if (!reading.BatteryPercentage.HasValue)
            return false;

        var pct = reading.BatteryPercentage.Value;
        var isAc = reading.IsAcConnected;
        var state = reading.ChargingState.HasValue ? reading.ChargingState.Value.ToString() : "Unknown";
        var now = reading.Timestamp;

        // Idle check: skip write if state is unchanged and idle interval hasn't elapsed
        lock (_throttleLock)
        {
            if (_lastRecorded != null)
            {
                var pctDelta = Math.Abs(_lastRecorded.Percentage - pct);
                var acUnchanged = _lastRecorded.IsAcConnected == isAc;
                var stateUnchanged = string.Equals(_lastRecorded.ChargingState, state, StringComparison.Ordinal);
                var elapsed = now - _lastRecordedAt;

                if (pctDelta < 0.5 && acUnchanged && stateUnchanged && elapsed < _minIdleWriteInterval)
                {
                    return false; // Throttled: no unnecessary disk write
                }
            }
        }

        var record = new BatteryReadingRecord
        {
            Timestamp = now,
            Percentage = pct,
            IsAcConnected = isAc,
            ChargingState = state,
            RemainingCapacityMwh = reading.RemainingCapacityMwh.HasValue ? reading.RemainingCapacityMwh.Value : null,
            FullChargeCapacityMwh = reading.FullChargeCapacityMwh.HasValue ? reading.FullChargeCapacityMwh.Value : null,
            DesignCapacityMwh = reading.DesignCapacityMwh.HasValue ? reading.DesignCapacityMwh.Value : null,
            ChargeRateMilliwatts = reading.ChargeRateMilliwatts.HasValue ? reading.ChargeRateMilliwatts.Value : null,
            CycleCount = reading.CycleCount.HasValue ? reading.CycleCount.Value : null,
            VoltageMillivolts = reading.VoltageMillivolts.HasValue ? reading.VoltageMillivolts.Value : null,
            ProviderSource = reading.TelemetryProviderSource,
            IsSimulated = reading.IsSimulated
        };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO BatteryTelemetry (
                Timestamp, Percentage, IsAcConnected, ChargingState,
                RemainingCapacityMwh, FullChargeCapacityMwh, DesignCapacityMwh,
                ChargeRateMilliwatts, CycleCount, VoltageMillivolts, ProviderSource, IsSimulated
            ) VALUES (
                $timestamp, $percentage, $isAc, $chargingState,
                $remCap, $fullCap, $designCap,
                $rate, $cycle, $voltage, $provider, $isSimulated
            );
            SELECT last_insert_rowid();
        ";

        cmd.Parameters.AddWithValue("$timestamp", record.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("$percentage", record.Percentage);
        cmd.Parameters.AddWithValue("$isAc", record.IsAcConnected ? 1 : 0);
        cmd.Parameters.AddWithValue("$chargingState", record.ChargingState);
        cmd.Parameters.AddWithValue("$remCap", (object?)record.RemainingCapacityMwh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fullCap", (object?)record.FullChargeCapacityMwh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$designCap", (object?)record.DesignCapacityMwh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$rate", (object?)record.ChargeRateMilliwatts ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cycle", (object?)record.CycleCount ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$voltage", (object?)record.VoltageMillivolts ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$provider", (object?)record.ProviderSource ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$isSimulated", record.IsSimulated ? 1 : 0);

        var insertedIdObj = await cmd.ExecuteScalarAsync(cancellationToken);
        var insertedId = Convert.ToInt64(insertedIdObj);

        var insertedRecord = record with { Id = insertedId };

        lock (_throttleLock)
        {
            _lastRecorded = insertedRecord;
            _lastRecordedAt = now;
        }

        return true;
    }

    public async Task<IReadOnlyList<BatteryReadingRecord>> GetRecentReadingsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var results = new List<BatteryReadingRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Timestamp, Percentage, IsAcConnected, ChargingState,
                   RemainingCapacityMwh, FullChargeCapacityMwh, DesignCapacityMwh,
                   ChargeRateMilliwatts, CycleCount, IsSimulated, VoltageMillivolts, ProviderSource
            FROM BatteryTelemetry
            ORDER BY Id DESC
            LIMIT $limit;
        ";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadRecord(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<BatteryReadingRecord>> GetReadingsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var results = new List<BatteryReadingRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Timestamp, Percentage, IsAcConnected, ChargingState,
                   RemainingCapacityMwh, FullChargeCapacityMwh, DesignCapacityMwh,
                   ChargeRateMilliwatts, CycleCount, IsSimulated, VoltageMillivolts, ProviderSource
            FROM BatteryTelemetry
            WHERE Timestamp >= $since
            ORDER BY Id ASC;
        ";
        cmd.Parameters.AddWithValue("$since", since.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadRecord(reader));
        }

        return results;
    }

    public async Task<int> PurgeOldReadingsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM BatteryTelemetry WHERE Timestamp < $olderThan;";
        cmd.Parameters.AddWithValue("$olderThan", olderThan.ToString("o"));

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static BatteryReadingRecord ReadRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Timestamp = DateTimeOffset.Parse(reader.GetString(1)),
        Percentage = reader.GetDouble(2),
        IsAcConnected = reader.GetInt32(3) == 1,
        ChargingState = reader.GetString(4),
        RemainingCapacityMwh = reader.IsDBNull(5) ? null : reader.GetDouble(5),
        FullChargeCapacityMwh = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        DesignCapacityMwh = reader.IsDBNull(7) ? null : reader.GetDouble(7),
        ChargeRateMilliwatts = reader.IsDBNull(8) ? null : reader.GetDouble(8),
        CycleCount = reader.IsDBNull(9) ? null : reader.GetInt32(9),
        IsSimulated = reader.GetInt32(10) == 1,
        VoltageMillivolts = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetDouble(11) : null,
        ProviderSource = reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetString(12) : null
    };
}
