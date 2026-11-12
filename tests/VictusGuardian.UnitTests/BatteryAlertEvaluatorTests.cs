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
