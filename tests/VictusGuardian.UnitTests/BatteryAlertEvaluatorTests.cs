using System;
using System.Linq;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;
using Xunit;

namespace VictusGuardian.UnitTests;

public class BatteryAlertEvaluatorTests
{
    private readonly DefaultBatteryAlertEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_CriticalBatteryOnBattery_ReturnsCriticalAlert()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(8.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Critical)
        };

        var alerts = _evaluator.Evaluate(reading);

        Assert.Contains(alerts, a => a.Level == BatteryAlertLevel.Critical);
    }

    [Fact]
    public void Evaluate_LowBatteryWhenConnectedToAc_DoesNotTriggerAlert()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(15.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Charging)
        };

        var alerts = _evaluator.Evaluate(reading);

        Assert.Empty(alerts);
    }

    [Fact]
    public void Evaluate_TargetChargeReached_ReturnsInfoAlert()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(82.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Charging)
        };

        var alerts = _evaluator.Evaluate(reading);

        Assert.Contains(alerts, a => a.Level == BatteryAlertLevel.Info && a.Title.Contains("Target Charge"));
    }

    [Fact]
    public void Evaluate_TargetChargeReached_DoesNotTriggerWhenDischarging()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(85.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging)
        };

        var alerts = _evaluator.Evaluate(reading);

        Assert.DoesNotContain(alerts, a => a.Category == BatteryAlertCategory.HighChargeReminder);
    }

    [Fact]
    public void Evaluate_Hysteresis_SuppressesRepeatedAlertUntilRearmed()
    {
        var evaluator = new DefaultBatteryAlertEvaluator();
        var t0 = DateTimeOffset.UtcNow;

        var readingLow = new BatteryReading
        {
            Timestamp = t0,
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(18.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging)
        };

        var firstAlerts = evaluator.Evaluate(readingLow);
        Assert.Contains(firstAlerts, a => a.Category == BatteryAlertCategory.LowBattery);

        // Immediate next tick at 17% (condition unchanged, within cooldown)
        var readingStillLow = readingLow with
        {
            Timestamp = t0.AddSeconds(10),
            BatteryPercentage = TelemetryValue<double>.Reported(17.0)
        };
        var secondAlerts = evaluator.Evaluate(readingStillLow);
        Assert.DoesNotContain(secondAlerts, a => a.Category == BatteryAlertCategory.LowBattery);

        // Battery recovers above threshold + hysteresis (20% + 5% = 25%)
        var readingCharged = readingLow with
        {
            Timestamp = t0.AddMinutes(20),
            BatteryPercentage = TelemetryValue<double>.Reported(26.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Charging)
        };
        evaluator.Evaluate(readingCharged);

        // Discharges back below low threshold after rearm
        var readingLowAgain = readingLow with
        {
            Timestamp = t0.AddMinutes(40),
            BatteryPercentage = TelemetryValue<double>.Reported(19.0)
        };
        var thirdAlerts = evaluator.Evaluate(readingLowAgain);
        Assert.Contains(thirdAlerts, a => a.Category == BatteryAlertCategory.LowBattery);
    }

    [Fact]
    public void Evaluate_StaleTelemetry_ReturnsStaleWarning()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(50.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging),
            IsStale = true
        };

        var alerts = _evaluator.Evaluate(reading);
        Assert.Contains(alerts, a => a.Category == BatteryAlertCategory.TelemetryStale);
    }

    [Fact]
    public void Evaluate_NoSensorsAvailable_ReturnsSensorUnavailableWarning()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.None,
            BatteryPercentage = TelemetryValue<double>.Unavailable,
            PowerStatus = TelemetryValue<AcPowerStatus>.Unavailable,
            ChargingState = TelemetryValue<BatteryChargingState>.Unavailable,
            TelemetryProviderSource = "Windows IOCTL"
        };

        var alerts = _evaluator.Evaluate(reading);
        Assert.Contains(alerts, a => a.Category == BatteryAlertCategory.SensorUnavailable);
    }

    [Fact]
    public void Evaluate_DegradedHealth_ReturnsWarningAlert()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(50.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging)
        };

        var health = new BatteryHealthReport
        {
            HealthPercentage = 64.0,
            WearPercentage = 36.0,
            Rating = BatteryHealthRating.Poor,
            CalculatedAt = DateTimeOffset.UtcNow,
            IsSimulated = false
        };

        var alerts = _evaluator.Evaluate(reading, health);

        Assert.Contains(alerts, a => a.Level == BatteryAlertLevel.Warning && a.Title.Contains("Health"));
    }
}
