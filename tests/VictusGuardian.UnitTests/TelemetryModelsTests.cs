using System;
using VictusGuardian.Core.Models;
using Xunit;

namespace VictusGuardian.UnitTests;

public class TelemetryModelsTests
{
    [Fact]
    public void TelemetryValue_Provenance_DistinguishesReportedAndSimulated()
    {
        var reported = TelemetryValue<double>.Reported(85.0);
        var simulated = TelemetryValue<double>.Simulated(85.0);
        var unavailable = TelemetryValue<double>.Unavailable;
        var estimated = TelemetryValue<double>.Estimated(85.0);

        Assert.True(reported.IsReported);
        Assert.False(reported.IsSimulated);

        Assert.True(simulated.IsSimulated);
        Assert.False(simulated.IsReported);

        Assert.True(unavailable.IsUnavailable);
        Assert.False(unavailable.HasValue);

        Assert.True(estimated.IsEstimated);
    }

    [Fact]
    public void BatteryReading_IsSimulated_ReturnsTrueWhenAnyMetricIsSimulated()
    {
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Simulated(50.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Charging)
        };

        Assert.True(reading.IsSimulated);
    }

    [Fact]
    public void BatteryReading_EstimatedTimeRemaining_CalculatesAccurately()
    {
        // 50,000 mWh remaining, discharging at 25,000 mW (25W) -> exactly 2.0 hours
        var reading = new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.All,
            RemainingCapacityMwh = TelemetryValue<double>.Reported(50000.0),
            FullChargeCapacityMwh = TelemetryValue<double>.Reported(70000.0),
            ChargeRateMilliwatts = TelemetryValue<double>.Reported(-25000.0),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline)
        };

        Assert.NotNull(reading.EstimatedTimeRemaining);
        Assert.Equal(2.0, reading.EstimatedTimeRemaining.Value.TotalHours, precision: 1);
        Assert.Equal(25.0, reading.RateInWatts);
    }
}
