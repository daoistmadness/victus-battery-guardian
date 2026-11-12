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

    Task<int> PurgeOldReadingsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
