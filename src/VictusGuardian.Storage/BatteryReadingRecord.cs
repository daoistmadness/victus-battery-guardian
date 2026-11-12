using System;

namespace VictusGuardian.Storage.Models;

public record BatteryReadingRecord
{
    public long Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required double Percentage { get; init; }
    public required bool IsAcConnected { get; init; }
    public required string ChargingState { get; init; }
    public double? RemainingCapacityMwh { get; init; }
    public double? FullChargeCapacityMwh { get; init; }
    public double? DesignCapacityMwh { get; init; }
    public double? ChargeRateMilliwatts { get; init; }
    public int? CycleCount { get; init; }
    public double? VoltageMillivolts { get; init; }
    public string? ProviderSource { get; init; }
    public required bool IsSimulated { get; init; }
}

public record ChargingSessionRecord
{
    public long Id { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
    public required double StartPercentage { get; init; }
    public double? EndPercentage { get; init; }
    public double? StartEnergyMwh { get; init; }
    public double? EndEnergyMwh { get; init; }
    public double? EnergyGainedMwh { get; init; }
    public double? DurationMinutes { get; init; }
    public string? ProviderSource { get; init; }
    public string? BatterySerialNumber { get; init; }
    public bool IsCompleted { get; init; }
}

public record BatteryHealthRecord
{
    public long Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required double FullChargeCapacityMwh { get; init; }
    public required double DesignCapacityMwh { get; init; }
    public int? CycleCount { get; init; }
    public string? BatterySerialNumber { get; init; }
    public string? BatteryName { get; init; }
    public string? Chemistry { get; init; }
}

public record BatteryUsageStats
{
    public double? AverageDischargeRateWatts { get; init; }
    public TimeSpan? EstimatedRuntimeFromHistory { get; init; }
    public double? TotalAcTimeHours { get; init; }
    public double? TotalBatteryTimeHours { get; init; }
    public double? PercentTimeAtHighCharge { get; init; }
    public int TotalChargingSessions { get; init; }
}
