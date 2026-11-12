using System;
using System.IO;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Repositories;
using Xunit;

namespace VictusGuardian.UnitTests;

public class SqliteBatteryHistoryRepositoryTests : IDisposable
{
    private readonly string _tempDbFile;
    private readonly SqliteBatteryHistoryRepository _repository;

    public SqliteBatteryHistoryRepositoryTests()
    {
        _tempDbFile = Path.Combine(Path.GetTempPath(), $"victus_test_{Guid.NewGuid():N}.db");
        _repository = new SqliteBatteryHistoryRepository($"Data Source={_tempDbFile}", TimeSpan.FromMinutes(10));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_tempDbFile))
        {
            try { File.Delete(_tempDbFile); } catch { }
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesSchemaSuccessfully()
    {
        await _repository.InitializeAsync();
        var readings = await _repository.GetRecentReadingsAsync();
        Assert.Empty(readings);
    }

    [Fact]
    public async Task RecordReadingAsync_InsertsRecordAndSupportsIdleThrottling()
    {
        await _repository.InitializeAsync();

        var reading1 = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(80.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging),
            RemainingCapacityMwh = TelemetryValue<double>.Reported(56000.0),
            FullChargeCapacityMwh = TelemetryValue<double>.Reported(70000.0),
            DesignCapacityMwh = TelemetryValue<double>.Reported(70000.0),
            ChargeRateMilliwatts = TelemetryValue<double>.Reported(-15000.0),
            CycleCount = TelemetryValue<int>.Reported(30)
        };

        // First insert succeeds
        var inserted1 = await _repository.RecordReadingAsync(reading1);
        Assert.True(inserted1);

        // Immediate identical reading within idle window is throttled
        var inserted2 = await _repository.RecordReadingAsync(reading1);
        Assert.False(inserted2);

        // Meaningful state change (plugged into AC) is recorded immediately
        var readingChanged = reading1 with
        {
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Charging)
        };

        var inserted3 = await _repository.RecordReadingAsync(readingChanged);
        Assert.True(inserted3);

        var recent = await _repository.GetRecentReadingsAsync();
        Assert.Equal(2, recent.Count);
        Assert.Equal(80.0, recent[0].Percentage);
    }

    [Fact]
    public async Task PurgeOldReadingsAsync_DeletesHistoricRecords()
    {
        await _repository.InitializeAsync();

        var oldReading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow.AddDays(-10),
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(50.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging)
        };

        await _repository.RecordReadingAsync(oldReading);

        var deletedCount = await _repository.PurgeOldReadingsAsync(DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Equal(1, deletedCount);

        var recent = await _repository.GetRecentReadingsAsync();
        Assert.Empty(recent);
    }
}
