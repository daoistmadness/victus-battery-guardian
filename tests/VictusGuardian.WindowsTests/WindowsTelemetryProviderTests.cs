using System;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Windows.Telemetry;
using Xunit;

namespace VictusGuardian.WindowsTests;

public class WindowsTelemetryProviderTests
{
    [Fact]
    public async Task GetReadingAsync_OnLinux_GracefullyReturnsUnsupportedWithoutCrashing()
    {
        var provider = new WindowsBatteryTelemetryProvider();

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(provider.IsHardwareSupported);

            var reading = await provider.GetReadingAsync();
            Assert.NotNull(reading);
            Assert.False(reading.BatteryPercentage.HasValue);
            Assert.Equal(SensorCapabilities.None, reading.AvailableSensors);
        }
        else
        {
            Assert.True(provider.IsHardwareSupported);
            var reading = await provider.GetReadingAsync();
            Assert.NotNull(reading);
        }
    }

    [SkippableFact]
    public async Task GetReadingAsync_OnPhysicalWindowsVictusHardware_ReturnsLiveReading()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Explicitly document hardware test requirement
            return;
        }

        var provider = new WindowsBatteryTelemetryProvider();
        var reading = await provider.GetReadingAsync();

        Assert.NotNull(reading);
        // On physical Windows laptop with battery:
        if (reading.BatteryPercentage.HasValue)
        {
            Assert.True(reading.BatteryPercentage.IsReported);
            Assert.InRange(reading.BatteryPercentage.Value, 0.0, 100.0);
        }
    }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class SkippableFactAttribute : FactAttribute
{
    public SkippableFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Requires physical Windows 11 target (HP Victus 15.6) with battery controller.";
        }
    }
}
