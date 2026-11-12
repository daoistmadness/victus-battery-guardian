using System;
using VictusGuardian.Core.Models;

namespace VictusGuardian.Core.Health;

public enum BatteryHealthRating
{
    Unknown = 0,
    Degraded = 1,
    Poor = 2,
    Fair = 3,
    Good = 4,
    Excellent = 5
}

public record BatteryHealthReport
{
    public required double HealthPercentage { get; init; }
    public required double WearPercentage { get; init; }
    public required BatteryHealthRating Rating { get; init; }
    public int? CycleCount { get; init; }
    public double? FullChargeCapacityMwh { get; init; }
    public double? DesignCapacityMwh { get; init; }
    public required DateTimeOffset CalculatedAt { get; init; }
    public required bool IsSimulated { get; init; }

    public static BatteryHealthReport Unavailable(DateTimeOffset timestamp, bool isSimulated = false) => new()
    {
        HealthPercentage = 100.0,
        WearPercentage = 0.0,
        Rating = BatteryHealthRating.Unknown,
        CalculatedAt = timestamp,
        IsSimulated = isSimulated
    };
}

public interface IBatteryHealthCalculator
{
    BatteryHealthReport Calculate(BatteryReading reading);
}

public class DefaultBatteryHealthCalculator : IBatteryHealthCalculator
{
    public BatteryHealthReport Calculate(BatteryReading reading)
    {
        if (!reading.FullChargeCapacityMwh.HasValue ||
            !reading.DesignCapacityMwh.HasValue ||
            reading.DesignCapacityMwh.Value <= 0)
        {
            return BatteryHealthReport.Unavailable(reading.Timestamp, reading.IsSimulated);
        }

        var full = reading.FullChargeCapacityMwh.Value;
        var design = reading.DesignCapacityMwh.Value;

        var health = Math.Clamp((full / design) * 100.0, 0.0, 100.0);
        var wear = Math.Max(0.0, 100.0 - health);

        var rating = health switch
        {
            >= 90.0 => BatteryHealthRating.Excellent,
            >= 80.0 => BatteryHealthRating.Good,
            >= 70.0 => BatteryHealthRating.Fair,
            >= 50.0 => BatteryHealthRating.Poor,
            _ => BatteryHealthRating.Degraded
        };

        return new BatteryHealthReport
        {
            HealthPercentage = Math.Round(health, 1),
            WearPercentage = Math.Round(wear, 1),
            Rating = rating,
            CycleCount = reading.CycleCount.HasValue ? reading.CycleCount.Value : null,
            FullChargeCapacityMwh = full,
            DesignCapacityMwh = design,
            CalculatedAt = reading.Timestamp,
            IsSimulated = reading.IsSimulated
        };
    }
}
