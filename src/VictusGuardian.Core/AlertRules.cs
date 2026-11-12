using System;
using System.Collections.Generic;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;

namespace VictusGuardian.Core.Alerts;

public enum BatteryAlertLevel
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

public record BatteryAlert(
    BatteryAlertLevel Level,
    string Title,
    string Message,
    DateTimeOffset Timestamp
);

public record AlertRuleSettings
{
    public double LowBatteryPercentage { get; init; } = 20.0;
    public double CriticalBatteryPercentage { get; init; } = 10.0;
    public double MaxChargeTargetPercentage { get; init; } = 80.0;
    public bool NotifyAtMaxChargeTarget { get; init; } = true;
    public double HealthDegradationThreshold { get; init; } = 70.0;
}

public interface IBatteryAlertEvaluator
{
    IReadOnlyList<BatteryAlert> Evaluate(BatteryReading reading, BatteryHealthReport? health = null, AlertRuleSettings? settings = null);
}

public class DefaultBatteryAlertEvaluator : IBatteryAlertEvaluator
{
    public IReadOnlyList<BatteryAlert> Evaluate(BatteryReading reading, BatteryHealthReport? health = null, AlertRuleSettings? settings = null)
    {
        settings ??= new AlertRuleSettings();
        var alerts = new List<BatteryAlert>();

        if (reading.BatteryPercentage.HasValue)
        {
            var pct = reading.BatteryPercentage.Value;

            if (pct <= settings.CriticalBatteryPercentage && !reading.IsAcConnected)
            {
                alerts.Add(new BatteryAlert(
                    BatteryAlertLevel.Critical,
                    "Battery Level Critical",
                    $"Battery is at {pct:0}%. Connect AC adapter immediately to prevent shutdown.",
                    reading.Timestamp));
            }
            else if (pct <= settings.LowBatteryPercentage && !reading.IsAcConnected)
            {
                alerts.Add(new BatteryAlert(
                    BatteryAlertLevel.Warning,
                    "Battery Level Low",
                    $"Battery is at {pct:0}%. Consider plugging in your Victus charger.",
                    reading.Timestamp));
            }
            else if (settings.NotifyAtMaxChargeTarget &&
                     pct >= settings.MaxChargeTargetPercentage &&
                     reading.ChargingState.Value == BatteryChargingState.Charging)
            {
                alerts.Add(new BatteryAlert(
                    BatteryAlertLevel.Info,
                    "Target Charge Reached",
                    $"Battery has reached {pct:0}% target. Unplugging helps extend lithium longevity.",
                    reading.Timestamp));
            }
        }

        if (health is not null && health.Rating != BatteryHealthRating.Unknown)
        {
            if (health.HealthPercentage <= settings.HealthDegradationThreshold)
            {
                alerts.Add(new BatteryAlert(
                    BatteryAlertLevel.Warning,
                    "Battery Health Degraded",
                    $"Battery health is at {health.HealthPercentage:0}%. Consider battery calibration or replacement.",
                    reading.Timestamp));
            }
        }

        return alerts;
    }
}
