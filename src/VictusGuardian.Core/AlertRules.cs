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

public enum BatteryAlertCategory
{
    LowBattery,
    CriticalBattery,
    HighChargeReminder,
    SensorUnavailable,
    TelemetryStale,
    HealthDegraded
}

public record BatteryAlert(
    BatteryAlertLevel Level,
    string Title,
    string Message,
    DateTimeOffset Timestamp,
    BatteryAlertCategory Category = BatteryAlertCategory.LowBattery
);

public record AlertRuleSettings
{
    public double LowBatteryPercentage { get; init; } = 20.0;
    public double CriticalBatteryPercentage { get; init; } = 10.0;
    public double MaxChargeTargetPercentage { get; init; } = 80.0;
    public bool NotifyAtMaxChargeTarget { get; init; } = true;
    public double HealthDegradationThreshold { get; init; } = 70.0;
    public double HysteresisPercentage { get; init; } = 5.0;
    public TimeSpan AlertCooldown { get; init; } = TimeSpan.FromMinutes(10);
    public bool EnableNotifications { get; init; } = true;
}

public interface IBatteryAlertEvaluator
{
    IReadOnlyList<BatteryAlert> Evaluate(BatteryReading reading, BatteryHealthReport? health = null, AlertRuleSettings? settings = null);
}

public class DefaultBatteryAlertEvaluator : IBatteryAlertEvaluator
{
    private readonly Dictionary<BatteryAlertCategory, DateTimeOffset> _lastFiredTimes = new();
    private bool _lowBatteryArmed = true;
    private bool _criticalBatteryArmed = true;
    private bool _highChargeArmed = true;

    public IReadOnlyList<BatteryAlert> Evaluate(BatteryReading reading, BatteryHealthReport? health = null, AlertRuleSettings? settings = null)
    {
        settings ??= new AlertRuleSettings();
        if (!settings.EnableNotifications)
        {
            return Array.Empty<BatteryAlert>();
        }

        var alerts = new List<BatteryAlert>();
        var now = reading.Timestamp;

        // 1. Stale Telemetry Warning
        if (reading.IsStale)
        {
            if (CanFire(BatteryAlertCategory.TelemetryStale, now, settings.AlertCooldown))
            {
                alerts.Add(new BatteryAlert(
                    BatteryAlertLevel.Warning,
                    "Telemetry Stale",
                    "Battery sensor telemetry has not updated within expected intervals.",
                    now,
                    BatteryAlertCategory.TelemetryStale));
                RecordFired(BatteryAlertCategory.TelemetryStale, now);
            }
        }

        // 2. Sensor Unavailable Warning
        if (reading.AvailableSensors == SensorCapabilities.None && !reading.IsSimulated)
        {
            if (CanFire(BatteryAlertCategory.SensorUnavailable, now, settings.AlertCooldown))
            {
                alerts.Add(new BatteryAlert(
                    BatteryAlertLevel.Warning,
                    "Sensors Unavailable",
                    "No battery telemetry sensors could be enumerated on this device.",
                    now,
                    BatteryAlertCategory.SensorUnavailable));
                RecordFired(BatteryAlertCategory.SensorUnavailable, now);
            }
        }

        // 3. Percentage-Based Alerts with Hysteresis
        if (reading.BatteryPercentage.HasValue)
        {
            var pct = reading.BatteryPercentage.Value;

            // Re-arm logic based on hysteresis
            if (reading.IsAcConnected || pct > (settings.CriticalBatteryPercentage + settings.HysteresisPercentage))
            {
                _criticalBatteryArmed = true;
            }

            if (reading.IsAcConnected || pct > (settings.LowBatteryPercentage + settings.HysteresisPercentage))
            {
                _lowBatteryArmed = true;
            }

            if (!reading.IsAcConnected || pct < (settings.MaxChargeTargetPercentage - settings.HysteresisPercentage))
            {
                _highChargeArmed = true;
            }

            // Critical Battery
            if (pct <= settings.CriticalBatteryPercentage && !reading.IsAcConnected)
            {
                if (_criticalBatteryArmed && CanFire(BatteryAlertCategory.CriticalBattery, now, settings.AlertCooldown))
                {
                    alerts.Add(new BatteryAlert(
                        BatteryAlertLevel.Critical,
                        "Battery Level Critical",
                        $"Battery is at {pct:0}%. Connect AC adapter immediately to prevent shutdown.",
                        now,
                        BatteryAlertCategory.CriticalBattery));
                    RecordFired(BatteryAlertCategory.CriticalBattery, now);
                    _criticalBatteryArmed = false;
                }
            }
            // Low Battery
            else if (pct <= settings.LowBatteryPercentage && !reading.IsAcConnected)
            {
                if (_lowBatteryArmed && CanFire(BatteryAlertCategory.LowBattery, now, settings.AlertCooldown))
                {
                    alerts.Add(new BatteryAlert(
                        BatteryAlertLevel.Warning,
                        "Battery Level Low",
                        $"Battery is at {pct:0}%. Consider plugging in your Victus charger.",
                        now,
                        BatteryAlertCategory.LowBattery));
                    RecordFired(BatteryAlertCategory.LowBattery, now);
                    _lowBatteryArmed = false;
                }
            }
            // High Charge Target (Advisory Reminder Only - does not stop charging)
            else if (settings.NotifyAtMaxChargeTarget &&
                     pct >= settings.MaxChargeTargetPercentage &&
                     reading.IsAcConnected)
            {
                if (_highChargeArmed && CanFire(BatteryAlertCategory.HighChargeReminder, now, settings.AlertCooldown))
                {
                    alerts.Add(new BatteryAlert(
                        BatteryAlertLevel.Info,
                        $"Target Charge Reached ({settings.MaxChargeTargetPercentage:0}%)",
                        $"Battery is at {pct:0}% while connected to AC power. Disconnecting AC preserves lithium longevity. (Note: Victus Guardian provides advisory guidance and does not modify BIOS or hardware charging behavior).",
                        now,
                        BatteryAlertCategory.HighChargeReminder));
                    RecordFired(BatteryAlertCategory.HighChargeReminder, now);
                    _highChargeArmed = false;
                }
            }
        }

        // 4. Health Degradation Warning
        if (health is not null && health.Rating != BatteryHealthRating.Unknown)
        {
            if (health.HealthPercentage <= settings.HealthDegradationThreshold)
            {
                if (CanFire(BatteryAlertCategory.HealthDegraded, now, settings.AlertCooldown))
                {
                    alerts.Add(new BatteryAlert(
                        BatteryAlertLevel.Warning,
                        "Battery Health Degraded",
                        $"Battery capacity retention is at {health.HealthPercentage:0}%. Consider recalibrating or replacing the pack.",
                        now,
                        BatteryAlertCategory.HealthDegraded));
                    RecordFired(BatteryAlertCategory.HealthDegraded, now);
                }
            }
        }

        return alerts;
    }

    private bool CanFire(BatteryAlertCategory category, DateTimeOffset now, TimeSpan cooldown)
    {
        if (!_lastFiredTimes.TryGetValue(category, out var lastFired))
            return true;

        return (now - lastFired) >= cooldown;
    }

    private void RecordFired(BatteryAlertCategory category, DateTimeOffset now)
    {
        _lastFiredTimes[category] = now;
    }

    public void ResetState()
    {
        _lastFiredTimes.Clear();
        _lowBatteryArmed = true;
        _criticalBatteryArmed = true;
        _highChargeArmed = true;
    }
}
