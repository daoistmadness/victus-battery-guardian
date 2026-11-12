using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Models;

namespace VictusGuardian.Storage.Repositories;

public interface IBatteryHistoryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a reading to the database, applying idle-write throttling to avoid redundant disk writes.
    /// Returns true if a row was inserted, false if throttled due to identical idle state.
    /// </summary>
    Task<bool> RecordReadingAsync(BatteryReading reading, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatteryReadingRecord>> GetRecentReadingsAsync(int limit = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatteryReadingRecord>> GetReadingsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    Task<int> GetReadingCountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatteryReadingRecord>> GetDownsampledReadingsAsync(DateTimeOffset since, int maxBuckets = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatteryReadingRecord>> GetRecentReadingsSinceAsync(DateTimeOffset since, int limit = 100, CancellationToken cancellationToken = default);

    Task<int> PurgeOldReadingsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    // Charging Session Tracking
    Task<ChargingSessionRecord?> GetActiveChargingSessionAsync(CancellationToken cancellationToken = default);
    Task<long> StartChargingSessionAsync(ChargingSessionRecord session, CancellationToken cancellationToken = default);
    Task<bool> CompleteChargingSessionAsync(long sessionId, DateTimeOffset endTime, double endPercentage, double? endEnergyMwh, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChargingSessionRecord>> GetRecentChargingSessionsAsync(int limit = 20, CancellationToken cancellationToken = default);

    // Battery Health History
    Task<bool> RecordHealthObservationAsync(BatteryHealthRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BatteryHealthRecord>> GetHealthObservationsAsync(int limit = 50, CancellationToken cancellationToken = default);

    // Aggregated Usage Analysis
    Task<BatteryUsageStats> GetUsageStatisticsAsync(TimeSpan period, CancellationToken cancellationToken = default);
}
