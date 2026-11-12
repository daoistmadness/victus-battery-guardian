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
