using System;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;
using Xunit;

namespace VictusGuardian.UnitTests;

public class BatteryHealthCalculatorTests
{
    private readonly DefaultBatteryHealthCalculator _calculator = new();

    [Fact]
    public void CalculateHealth_HpVictusProfile_ReturnsExcellentRating()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            DesignCapacityMwh = TelemetryValue<double>.Reported(70070.0),
            FullChargeCapacityMwh = TelemetryValue<double>.Reported(66500.0),
            CycleCount = TelemetryValue<int>.Reported(42)
        };

        var health = _calculator.Calculate(reading);

        Assert.Equal(94.9, health.HealthPercentage, precision: 1);
        Assert.Equal(5.1, health.WearPercentage, precision: 1);
        Assert.Equal(BatteryHealthRating.Excellent, health.Rating);
        Assert.Equal(42, health.CycleCount);
        Assert.False(health.IsSimulated);
    }

    [Fact]
    public void CalculateHealth_DegradedBattery_ReturnsDegradedRating()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            DesignCapacityMwh = TelemetryValue<double>.Reported(70000.0),
            FullChargeCapacityMwh = TelemetryValue<double>.Reported(30000.0), // ~42.9% health
            CycleCount = TelemetryValue<int>.Reported(850)
        };

        var health = _calculator.Calculate(reading);

        Assert.True(health.HealthPercentage < 50.0);
        Assert.Equal(BatteryHealthRating.Degraded, health.Rating);
    }

    [Fact]
    public void CalculateHealth_MissingCapacities_ReturnsUnknownRating()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.Percentage,
            DesignCapacityMwh = TelemetryValue<double>.Unavailable,
            FullChargeCapacityMwh = TelemetryValue<double>.Unavailable
        };

        var health = _calculator.Calculate(reading);

        Assert.Equal(BatteryHealthRating.Unknown, health.Rating);
    }
}
