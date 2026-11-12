using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;

namespace VictusGuardian.Core.Contracts;

public interface IBatteryTelemetryProvider
{
    string ProviderName { get; }
    bool IsHardwareSupported { get; }

    Task<BatteryReading> GetReadingAsync(CancellationToken cancellationToken = default);

    async IAsyncEnumerable<BatteryReading> StreamReadingsAsync(
        TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return await GetReadingAsync(cancellationToken);
            await Task.Delay(interval, cancellationToken);
        }
    }
}
