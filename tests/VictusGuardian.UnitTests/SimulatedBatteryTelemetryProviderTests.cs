using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Core.Simulation;
using Xunit;

namespace VictusGuardian.UnitTests;

public class SimulatedBatteryTelemetryProviderTests
{
    [Fact]
    public async Task GetReadingAsync_DefaultHpVictusProfile_ReturnsSimulatedValues()
    {
        var provider = new SimulatedBatteryTelemetryProvider();

        var reading = await provider.GetReadingAsync();

        Assert.True(reading.IsSimulated);
        Assert.True(reading.BatteryPercentage.IsSimulated);
        Assert.True(reading.PowerStatus.IsSimulated);
        Assert.Equal(78.5, reading.BatteryPercentage.Value);
        Assert.Equal(70070.0, reading.DesignCapacityMwh.Value);
        Assert.Equal(42, reading.CycleCount.Value);
        Assert.Equal(AcPowerStatus.Offline, reading.PowerStatus.Value);
        Assert.Equal(BatteryChargingState.Discharging, reading.ChargingState.Value);
    }

    [Fact]
    public async Task SetAcConnected_TogglesChargingStateAndRate()
    {
        var provider = new SimulatedBatteryTelemetryProvider();

        provider.SetAcConnected(true);
        provider.SetChargeRateWatts(45.0);
        var reading = await provider.GetReadingAsync();

        Assert.True(reading.IsAcConnected);
        Assert.Equal(BatteryChargingState.Charging, reading.ChargingState.Value);
        Assert.True(reading.ChargeRateMilliwatts.Value > 0);
        Assert.Equal(45.0, reading.RateInWatts);
    }

    [Fact]
    public async Task SetPercentage_EnforcesBounds()
    {
        var provider = new SimulatedBatteryTelemetryProvider();

        provider.SetPercentage(120.0);
        var readingMax = await provider.GetReadingAsync();
        Assert.Equal(100.0, readingMax.BatteryPercentage.Value);

        provider.SetPercentage(-10.0);
        var readingMin = await provider.GetReadingAsync();
        Assert.Equal(0.0, readingMin.BatteryPercentage.Value);
    }
}
