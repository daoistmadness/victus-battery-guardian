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

        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA wal_autocheckpoint = 250;";
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
            currentVersion = 2;
        }

        if (currentVersion < 3)
        {
            using var tx = connection.BeginTransaction();
            using var v3Cmd = connection.CreateCommand();
            v3Cmd.Transaction = tx;
            v3Cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ChargingSessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StartTime TEXT NOT NULL,
                    EndTime TEXT,
                    StartPercentage REAL NOT NULL,
                    EndPercentage REAL,
                    StartEnergyMwh REAL,
                    EndEnergyMwh REAL,
                    EnergyGainedMwh REAL,
                    DurationMinutes REAL,
                    ProviderSource TEXT,
                    BatterySerialNumber TEXT,
                    IsCompleted INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_ChargingSessions_StartTime ON ChargingSessions (StartTime);

                CREATE TABLE IF NOT EXISTS BatteryHealthHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    FullChargeCapacityMwh REAL NOT NULL,
                    DesignCapacityMwh REAL NOT NULL,
                    CycleCount INTEGER,
                    BatterySerialNumber TEXT,
                    BatteryName TEXT,
                    Chemistry TEXT
                );
                CREATE INDEX IF NOT EXISTS IX_BatteryHealthHistory_Timestamp ON BatteryHealthHistory (Timestamp);
                PRAGMA user_version = 3;
            ";
            await v3Cmd.ExecuteNonQueryAsync(cancellationToken);
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

        lock (_throttleLock)
        {
            if (_lastRecorded != null)
            {
                var pctDelta = Math.Abs(_lastRecorded.Percentage - pct);
                var acUnchanged = _lastRecorded.IsAcConnected == isAc;
                var stateUnchanged = string.Equals(_lastRecorded.ChargingState, state, StringComparison.Ordinal);
                var providerUnchanged = string.Equals(_lastRecorded.ProviderSource, reading.TelemetryProviderSource, StringComparison.Ordinal);
                var simUnchanged = _lastRecorded.IsSimulated == reading.IsSimulated;

                var readingVoltage = reading.VoltageMillivolts.HasValue ? reading.VoltageMillivolts.Value : (double?)null;
                var voltageDelta = (_lastRecorded.VoltageMillivolts.HasValue && readingVoltage.HasValue)
                    ? Math.Abs(_lastRecorded.VoltageMillivolts.Value - readingVoltage.Value)
                    : (_lastRecorded.VoltageMillivolts.HasValue != readingVoltage.HasValue ? 1000.0 : 0.0);
                var voltageUnchanged = voltageDelta < 250.0; // 250 mV threshold for significant electrical change

                var elapsed = now - _lastRecordedAt;

                // Write suppression: skip disk write if conditions are stable and heartbeat interval has not elapsed
                if (pctDelta < 0.5 && acUnchanged && stateUnchanged && voltageUnchanged && providerUnchanged && simUnchanged && elapsed < _minIdleWriteInterval)
                {
                    return false;
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
            ORDER BY Timestamp ASC;
        ";
        cmd.Parameters.AddWithValue("$since", since.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadRecord(reader));
        }

        return results;
    }

    public async Task<int> GetReadingCountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM BatteryTelemetry WHERE Timestamp >= $since;";
        cmd.Parameters.AddWithValue("$since", since.ToString("o"));

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<BatteryReadingRecord>> GetRecentReadingsSinceAsync(DateTimeOffset since, int limit = 100, CancellationToken cancellationToken = default)
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
            ORDER BY Id DESC
            LIMIT $limit;
        ";
        cmd.Parameters.AddWithValue("$since", since.ToString("o"));
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadRecord(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<BatteryReadingRecord>> GetDownsampledReadingsAsync(DateTimeOffset since, int maxBuckets = 100, CancellationToken cancellationToken = default)
    {
        var totalCount = await GetReadingCountSinceAsync(since, cancellationToken);
        if (totalCount <= maxBuckets)
        {
            return await GetReadingsSinceAsync(since, cancellationToken);
        }

        int stride = Math.Max(1, totalCount / maxBuckets);
        var results = new List<BatteryReadingRecord>(Math.Min(totalCount, maxBuckets + 10));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            WITH Numbered AS (
                SELECT Id, Timestamp, Percentage, IsAcConnected, ChargingState,
                       RemainingCapacityMwh, FullChargeCapacityMwh, DesignCapacityMwh,
                       ChargeRateMilliwatts, CycleCount, IsSimulated, VoltageMillivolts, ProviderSource,
                       ROW_NUMBER() OVER (ORDER BY Timestamp ASC) as RowNum
                FROM BatteryTelemetry
                WHERE Timestamp >= $since
            )
            SELECT Id, Timestamp, Percentage, IsAcConnected, ChargingState,
                   RemainingCapacityMwh, FullChargeCapacityMwh, DesignCapacityMwh,
                   ChargeRateMilliwatts, CycleCount, IsSimulated, VoltageMillivolts, ProviderSource
            FROM Numbered
            WHERE RowNum % $stride = 0 OR RowNum = 1 OR RowNum = $totalCount
            ORDER BY Timestamp ASC;
        ";
        cmd.Parameters.AddWithValue("$since", since.ToString("o"));
        cmd.Parameters.AddWithValue("$stride", stride);
        cmd.Parameters.AddWithValue("$totalCount", totalCount);

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

    public async Task<ChargingSessionRecord?> GetActiveChargingSessionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, StartTime, EndTime, StartPercentage, EndPercentage,
                   StartEnergyMwh, EndEnergyMwh, EnergyGainedMwh, DurationMinutes,
                   ProviderSource, BatterySerialNumber, IsCompleted
            FROM ChargingSessions
            WHERE IsCompleted = 0
            ORDER BY StartTime DESC
            LIMIT 1;
        ";

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadChargingSession(reader);
        }

        return null;
    }

    public async Task<long> StartChargingSessionAsync(ChargingSessionRecord session, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ChargingSessions (
                StartTime, StartPercentage, StartEnergyMwh, ProviderSource, BatterySerialNumber, IsCompleted
            ) VALUES (
                $startTime, $startPct, $startEnergy, $provider, $serial, 0
            );
            SELECT last_insert_rowid();
        ";

        cmd.Parameters.AddWithValue("$startTime", session.StartTime.ToString("o"));
        cmd.Parameters.AddWithValue("$startPct", session.StartPercentage);
        cmd.Parameters.AddWithValue("$startEnergy", (object?)session.StartEnergyMwh ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$provider", (object?)session.ProviderSource ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$serial", (object?)session.BatterySerialNumber ?? DBNull.Value);

        var idObj = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(idObj);
    }

    public async Task<bool> CompleteChargingSessionAsync(long sessionId, DateTimeOffset endTime, double endPercentage, double? endEnergyMwh, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Fetch start info to calculate energy gained and duration
        using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT StartTime, StartEnergyMwh FROM ChargingSessions WHERE Id = $id;";
        selectCmd.Parameters.AddWithValue("$id", sessionId);

        DateTimeOffset startTime = endTime;
        double? startEnergy = null;
        using (var reader = await selectCmd.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                startTime = DateTimeOffset.Parse(reader.GetString(0));
                if (!reader.IsDBNull(1)) startEnergy = reader.GetDouble(1);
            }
            else
            {
                return false;
            }
        }

        var durationMinutes = Math.Max(0.0, (endTime - startTime).TotalMinutes);
        double? energyGained = null;
        if (endEnergyMwh.HasValue && startEnergy.HasValue)
        {
            energyGained = Math.Max(0.0, endEnergyMwh.Value - startEnergy.Value);
        }

        using var updateCmd = connection.CreateCommand();
        updateCmd.CommandText = @"
            UPDATE ChargingSessions
            SET EndTime = $endTime,
                EndPercentage = $endPct,
                EndEnergyMwh = $endEnergy,
                EnergyGainedMwh = $energyGained,
                DurationMinutes = $duration,
                IsCompleted = 1
            WHERE Id = $id;
        ";

        updateCmd.Parameters.AddWithValue("$id", sessionId);
        updateCmd.Parameters.AddWithValue("$endTime", endTime.ToString("o"));
        updateCmd.Parameters.AddWithValue("$endPct", endPercentage);
        updateCmd.Parameters.AddWithValue("$endEnergy", (object?)endEnergyMwh ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("$energyGained", (object?)energyGained ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("$duration", durationMinutes);

        var rows = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<IReadOnlyList<ChargingSessionRecord>> GetRecentChargingSessionsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        var results = new List<ChargingSessionRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, StartTime, EndTime, StartPercentage, EndPercentage,
                   StartEnergyMwh, EndEnergyMwh, EnergyGainedMwh, DurationMinutes,
                   ProviderSource, BatterySerialNumber, IsCompleted
            FROM ChargingSessions
            ORDER BY StartTime DESC
            LIMIT $limit;
        ";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadChargingSession(reader));
        }

        return results;
    }

    public async Task<bool> RecordHealthObservationAsync(BatteryHealthRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Check latest record to prevent inserting identical measurements
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = @"
            SELECT FullChargeCapacityMwh, CycleCount, BatterySerialNumber
            FROM BatteryHealthHistory
            ORDER BY Timestamp DESC
            LIMIT 1;
        ";

        using (var reader = await checkCmd.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                var lastFull = reader.GetDouble(0);
                int? lastCycle = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                string? lastSerial = reader.IsDBNull(2) ? null : reader.GetString(2);

                if (Math.Abs(lastFull - record.FullChargeCapacityMwh) < 1.0 &&
                    lastCycle == record.CycleCount &&
                    string.Equals(lastSerial, record.BatterySerialNumber, StringComparison.OrdinalIgnoreCase))
                {
                    return false; // Redundant observation
                }
            }
        }

        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO BatteryHealthHistory (
                Timestamp, FullChargeCapacityMwh, DesignCapacityMwh, CycleCount, BatterySerialNumber, BatteryName, Chemistry
            ) VALUES (
                $timestamp, $fullCap, $designCap, $cycles, $serial, $name, $chem
            );
        ";

        insertCmd.Parameters.AddWithValue("$timestamp", record.Timestamp.ToString("o"));
        insertCmd.Parameters.AddWithValue("$fullCap", record.FullChargeCapacityMwh);
        insertCmd.Parameters.AddWithValue("$designCap", record.DesignCapacityMwh);
        insertCmd.Parameters.AddWithValue("$cycles", (object?)record.CycleCount ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("$serial", (object?)record.BatterySerialNumber ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("$name", (object?)record.BatteryName ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("$chem", (object?)record.Chemistry ?? DBNull.Value);

        var rows = await insertCmd.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<IReadOnlyList<BatteryHealthRecord>> GetHealthObservationsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var results = new List<BatteryHealthRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Timestamp, FullChargeCapacityMwh, DesignCapacityMwh, CycleCount, BatterySerialNumber, BatteryName, Chemistry
            FROM BatteryHealthHistory
            ORDER BY Timestamp DESC
            LIMIT $limit;
        ";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new BatteryHealthRecord
            {
                Id = reader.GetInt64(0),
                Timestamp = DateTimeOffset.Parse(reader.GetString(1)),
                FullChargeCapacityMwh = reader.GetDouble(2),
                DesignCapacityMwh = reader.GetDouble(3),
                CycleCount = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                BatterySerialNumber = reader.IsDBNull(5) ? null : reader.GetString(5),
                BatteryName = reader.IsDBNull(6) ? null : reader.GetString(6),
                Chemistry = reader.IsDBNull(7) ? null : reader.GetString(7)
            });
        }

        return results;
    }

    public async Task<BatteryUsageStats> GetUsageStatisticsAsync(TimeSpan period, CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow - period;
        var readings = await GetReadingsSinceAsync(since, cancellationToken);
        var sessions = await GetRecentChargingSessionsAsync(50, cancellationToken);

        if (readings.Count == 0)
        {
            return new BatteryUsageStats { TotalChargingSessions = sessions.Count };
        }

        double acMinutes = 0;
        double batteryMinutes = 0;
        double highChargeMinutes = 0;
        double totalDischargeMwh = 0;
        double totalDischargeHours = 0;
        var dischargeWattsSamples = new List<double>();

        for (int i = 0; i < readings.Count - 1; i++)
        {
            var cur = readings[i];
            var next = readings[i + 1];
            var dtMinutes = Math.Clamp((next.Timestamp - cur.Timestamp).TotalMinutes, 0.0, 30.0);

            if (cur.IsAcConnected)
            {
                acMinutes += dtMinutes;
            }
            else
            {
                batteryMinutes += dtMinutes;
            }

            if (cur.Percentage >= 80.0)
            {
                highChargeMinutes += dtMinutes;
            }

            if (!cur.IsAcConnected && cur.ChargeRateMilliwatts.HasValue && cur.ChargeRateMilliwatts.Value < 0)
            {
                dischargeWattsSamples.Add(Math.Abs(cur.ChargeRateMilliwatts.Value) / 1000.0);
            }
            else if (!cur.IsAcConnected && cur.RemainingCapacityMwh.HasValue && next.RemainingCapacityMwh.HasValue)
            {
                var drop = cur.RemainingCapacityMwh.Value - next.RemainingCapacityMwh.Value;
                var dtHours = (next.Timestamp - cur.Timestamp).TotalHours;
                if (drop > 0 && dtHours > 0.01 && dtHours < 0.5)
                {
                    totalDischargeMwh += drop;
                    totalDischargeHours += dtHours;
                }
            }
        }

        double? avgDischargeWatts = null;
        if (dischargeWattsSamples.Count > 0)
        {
            double sum = 0;
            foreach (var w in dischargeWattsSamples) sum += w;
            avgDischargeWatts = sum / dischargeWattsSamples.Count;
        }
        else if (totalDischargeHours > 0.05 && totalDischargeMwh > 0)
        {
            // Fallback: estimate average watts from observed capacity drop over time
            avgDischargeWatts = (totalDischargeMwh / totalDischargeHours) / 1000.0;
        }

        TimeSpan? estimatedRuntime = null;
        var latest = readings[^1];
        if (avgDischargeWatts.HasValue && avgDischargeWatts.Value > 0.5 && latest.RemainingCapacityMwh.HasValue && !latest.IsAcConnected)
        {
            var remainingHours = (latest.RemainingCapacityMwh.Value / 1000.0) / avgDischargeWatts.Value;
            if (remainingHours > 0 && remainingHours < 24)
            {
                estimatedRuntime = TimeSpan.FromHours(remainingHours);
            }
        }

        var totalObservedMinutes = acMinutes + batteryMinutes;
        double? percentHigh = totalObservedMinutes > 0 ? (highChargeMinutes / totalObservedMinutes) * 100.0 : null;

        return new BatteryUsageStats
        {
            AverageDischargeRateWatts = avgDischargeWatts.HasValue ? Math.Round(avgDischargeWatts.Value, 1) : null,
            EstimatedRuntimeFromHistory = estimatedRuntime,
            TotalAcTimeHours = Math.Round(acMinutes / 60.0, 1),
            TotalBatteryTimeHours = Math.Round(batteryMinutes / 60.0, 1),
            PercentTimeAtHighCharge = percentHigh.HasValue ? Math.Round(percentHigh.Value, 1) : null,
            TotalChargingSessions = sessions.Count
        };
    }

    private static BatteryReadingRecord ReadRecord(SqliteDataReader reader)
    {
        return new BatteryReadingRecord
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
            VoltageMillivolts = reader.IsDBNull(11) ? null : reader.GetDouble(11),
            ProviderSource = reader.IsDBNull(12) ? null : reader.GetString(12)
        };
    }

    private static ChargingSessionRecord ReadChargingSession(SqliteDataReader reader)
    {
        return new ChargingSessionRecord
        {
            Id = reader.GetInt64(0),
            StartTime = DateTimeOffset.Parse(reader.GetString(1)),
            EndTime = reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
            StartPercentage = reader.GetDouble(3),
            EndPercentage = reader.IsDBNull(4) ? null : reader.GetDouble(4),
            StartEnergyMwh = reader.IsDBNull(5) ? null : reader.GetDouble(5),
            EndEnergyMwh = reader.IsDBNull(6) ? null : reader.GetDouble(6),
            EnergyGainedMwh = reader.IsDBNull(7) ? null : reader.GetDouble(7),
            DurationMinutes = reader.IsDBNull(8) ? null : reader.GetDouble(8),
            ProviderSource = reader.IsDBNull(9) ? null : reader.GetString(9),
            BatterySerialNumber = reader.IsDBNull(10) ? null : reader.GetString(10),
            IsCompleted = reader.GetInt32(11) == 1
        };
    }
}
