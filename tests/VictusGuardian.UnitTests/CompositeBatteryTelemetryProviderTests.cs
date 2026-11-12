using System;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Core.Simulation;
using VictusGuardian.Windows.Composite;
using VictusGuardian.Windows.Power;
using VictusGuardian.Windows.Telemetry;
using Xunit;

namespace VictusGuardian.UnitTests;

public class CompositeBatteryTelemetryProviderTests
{
    [Fact]
    public async Task GetReadingAsync_IoctlAvailable_UsesPreferredIoctlTier()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        var ioctlReader = new WindowsIoctlBatteryReader(mockApi);
        var powerReader = new WindowsPowerStatusReader(mockApi);
        var simProvider = new SimulatedBatteryTelemetryProvider();
        using var composite = new CompositeBatteryTelemetryProvider(ioctlReader, powerReader, simProvider);

        var reading = await composite.GetReadingAsync();

        Assert.Equal("Windows IOCTL (Direct Device Interface)", composite.ActiveProviderTier);
        Assert.False(composite.IsDemoModeEnabled);
        Assert.False(reading.IsSimulated);
        Assert.NotNull(composite.LastSuccessfulReadingTime);
    }

    [Fact]
    public async Task GetReadingAsync_IoctlFails_FallsBackToWin32PowerStatus()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.ShouldFailOpen = true; // IOCTL device open fails
        var ioctlReader = new WindowsIoctlBatteryReader(mockApi);
        var powerReader = new WindowsPowerStatusReader(mockApi);
        var simProvider = new SimulatedBatteryTelemetryProvider();
        using var composite = new CompositeBatteryTelemetryProvider(ioctlReader, powerReader, simProvider);

        var reading = await composite.GetReadingAsync();

        Assert.Equal("Windows Win32 (GetSystemPowerStatus Fallback)", composite.ActiveProviderTier);
        Assert.False(reading.IsSimulated);
        Assert.True(reading.BatteryPercentage.IsReported);
        Assert.Equal(78.0, reading.BatteryPercentage.Value);
    }

    [Fact]
    public async Task GetReadingAsync_BothTiersFail_ReturnsExplicitUnavailable_NeverSilentSimulation()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.IsSupported = false; // Non-Windows or completely failed native API
        var ioctlReader = new WindowsIoctlBatteryReader(mockApi);
        var powerReader = new WindowsPowerStatusReader(mockApi);
        var simProvider = new SimulatedBatteryTelemetryProvider();
        using var composite = new CompositeBatteryTelemetryProvider(ioctlReader, powerReader, simProvider);

        var reading = await composite.GetReadingAsync();

        // Must NEVER silently switch to simulated data
        Assert.Equal("Telemetry Unavailable", composite.ActiveProviderTier);
        Assert.False(reading.IsSimulated);
        Assert.True(reading.BatteryPercentage.IsUnavailable);
        Assert.Equal(SensorCapabilities.None, reading.AvailableSensors);
        Assert.Contains("Telemetry Unavailable", reading.TelemetryProviderSource);
    }

    [Fact]
    public async Task GetReadingAsync_ExplicitDemoModeEnabled_ReturnsSimulatedTelemetry()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        var ioctlReader = new WindowsIoctlBatteryReader(mockApi);
        var powerReader = new WindowsPowerStatusReader(mockApi);
        var simProvider = new SimulatedBatteryTelemetryProvider();
        using var composite = new CompositeBatteryTelemetryProvider(ioctlReader, powerReader, simProvider);

        composite.IsDemoModeEnabled = true;
        var reading = await composite.GetReadingAsync();

        Assert.Equal("Demo Mode (Simulated)", composite.ActiveProviderTier);
        Assert.True(reading.IsSimulated);
        Assert.Contains("Demo Mode (Simulated)", reading.TelemetryProviderSource);
    }

    [Fact]
    public async Task SuspendResumeEvent_InvalidatesIoctlTagCache()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        var ioctlReader = new WindowsIoctlBatteryReader(mockApi);
        var powerReader = new WindowsPowerStatusReader(mockApi);
        var simProvider = new SimulatedBatteryTelemetryProvider();
        var powerEvents = new WindowsPowerEventNotifier();
        using var composite = new CompositeBatteryTelemetryProvider(ioctlReader, powerReader, simProvider, powerEvents);

        await composite.GetReadingAsync();
        Assert.NotNull(ioctlReader.LastKnownBatteryTag);

        // Fire Suspend
        powerEvents.NotifyStateChange(PowerStateChangeType.Suspend);
        Assert.Null(ioctlReader.LastKnownBatteryTag);

        // Read again after resume
        await composite.GetReadingAsync();
        Assert.NotNull(ioctlReader.LastKnownBatteryTag);

        // Fire Resume
        powerEvents.NotifyStateChange(PowerStateChangeType.Resume);
        Assert.Null(ioctlReader.LastKnownBatteryTag);
    }

    [Fact]
    public async Task StalenessDetection_WhenTimeExceedsThreshold_MarksReadingAsStale()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        var ioctlReader = new WindowsIoctlBatteryReader(mockApi);
        var powerReader = new WindowsPowerStatusReader(mockApi);
        var simProvider = new SimulatedBatteryTelemetryProvider();
        using var composite = new CompositeBatteryTelemetryProvider(ioctlReader, powerReader, simProvider)
        {
            StalenessThreshold = TimeSpan.FromMilliseconds(50)
        };

        // First successful read
        await composite.GetReadingAsync();
        Assert.False(composite.IsReadingStale);

        // Wait past threshold
        await Task.Delay(100);
        Assert.True(composite.IsReadingStale);
    }
}
