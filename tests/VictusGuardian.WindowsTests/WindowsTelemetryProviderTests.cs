using System;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Core.Simulation;
using VictusGuardian.Windows.Composite;
using VictusGuardian.Windows.Native;
using VictusGuardian.Windows.Power;
using VictusGuardian.Windows.Telemetry;
using Xunit;

namespace VictusGuardian.WindowsTests;

public class WindowsTelemetryProviderTests
{
    [Fact]
    public async Task GetReadingAsync_OnLinux_GracefullyReturnsUnsupportedWithoutCrashing()
    {
        var nativeApi = OperatingSystem.IsWindows()
            ? (INativeBatteryDeviceApi)new WindowsNativeBatteryDeviceApi()
            : new UnsupportedNativeBatteryDeviceApi();

        var ioctlReader = new WindowsIoctlBatteryReader(nativeApi);
        var powerReader = new WindowsPowerStatusReader(nativeApi);
        var simProvider = new SimulatedBatteryTelemetryProvider();
        using var composite = new CompositeBatteryTelemetryProvider(ioctlReader, powerReader, simProvider);

        if (!OperatingSystem.IsWindows())
        {
            Assert.False(composite.IsHardwareSupported);

            var reading = await composite.GetReadingAsync();
            Assert.NotNull(reading);
            Assert.False(reading.BatteryPercentage.HasValue);
            Assert.Equal(SensorCapabilities.None, reading.AvailableSensors);
            Assert.Contains("Telemetry Unavailable", reading.TelemetryProviderSource);
        }
        else
        {
            Assert.True(composite.IsHardwareSupported);
            var reading = await composite.GetReadingAsync();
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

        var nativeApi = new WindowsNativeBatteryDeviceApi();
        var ioctlReader = new WindowsIoctlBatteryReader(nativeApi);
        var powerReader = new WindowsPowerStatusReader(nativeApi);
        var simProvider = new SimulatedBatteryTelemetryProvider();
        using var composite = new CompositeBatteryTelemetryProvider(ioctlReader, powerReader, simProvider);

        var reading = await composite.GetReadingAsync();

        Assert.NotNull(reading);
        // On physical Windows laptop with battery:
        if (reading.BatteryPercentage.HasValue)
        {
            Assert.True(reading.BatteryPercentage.IsReported);
            Assert.InRange(reading.BatteryPercentage.Value, 0.0, 100.0);
            Assert.Contains("Windows", reading.TelemetryProviderSource);
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
