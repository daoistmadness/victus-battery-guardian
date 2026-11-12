using System;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Windows.Native;
using VictusGuardian.Windows.Telemetry;
using Xunit;

namespace VictusGuardian.UnitTests;

public class WindowsIoctlBatteryReaderTests
{
    [Fact]
    public async Task ReadTelemetryAsync_ValidBattery_ReturnsReportedTelemetry()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.NotNull(reading);
        Assert.True(reading.BatteryPercentage.IsReported);
        Assert.Equal(78.5, reading.BatteryPercentage.Value);
        Assert.True(reading.RemainingCapacityMwh.IsReported);
        Assert.Equal(52202.0, reading.RemainingCapacityMwh.Value);
        Assert.True(reading.FullChargeCapacityMwh.IsReported);
        Assert.Equal(66500.0, reading.FullChargeCapacityMwh.Value);
        Assert.True(reading.DesignCapacityMwh.IsReported);
        Assert.Equal(70070.0, reading.DesignCapacityMwh.Value);
        Assert.True(reading.VoltageMillivolts.IsReported);
        Assert.Equal(15400.0, reading.VoltageMillivolts.Value);
        Assert.Equal(15.4, reading.VoltageInVolts);
        Assert.True(reading.CycleCount.IsReported);
        Assert.Equal(42, reading.CycleCount.Value);
        Assert.True(reading.BatteryName.IsReported);
        Assert.Equal("Primary", reading.BatteryName.Value);
        Assert.True(reading.Manufacturer.IsReported);
        Assert.Equal("HP / Celxpert", reading.Manufacturer.Value);
        Assert.True(reading.SerialNumber.IsReported);
        Assert.Equal("SN-2024-TEST", reading.SerialNumber.Value);
        Assert.True(reading.Chemistry.IsReported);
        Assert.Equal("LION", reading.Chemistry.Value);
        Assert.False(reading.IsSimulated);
        Assert.False(reading.IsStale);
        Assert.Equal(1u, reading.BatteryTag);
        Assert.Contains("Direct Device Interface", reading.TelemetryProviderSource);
    }

    [Fact]
    public async Task ReadTelemetryAsync_DischargingState_ProducesNegativeChargeRate()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.SimulatedStatus = mockApi.SimulatedStatus with
        {
            PowerState = BatteryNativeConstants.BATTERY_DISCHARGING,
            Rate = 18500
        };
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.Equal(BatteryChargingState.Discharging, reading.ChargingState.Value);
        Assert.True(reading.ChargeRateMilliwatts.IsReported);
        Assert.Equal(-18500.0, reading.ChargeRateMilliwatts.Value);
        Assert.Equal(18.5, reading.RateInWatts);
        Assert.Equal(AcPowerStatus.Offline, reading.PowerStatus.Value);
    }

    [Fact]
    public async Task ReadTelemetryAsync_ChargingState_ProducesPositiveChargeRate()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.SimulatedStatus = mockApi.SimulatedStatus with
        {
            PowerState = BatteryNativeConstants.BATTERY_POWER_ON_LINE | BatteryNativeConstants.BATTERY_CHARGING,
            Rate = 45000
        };
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.Equal(BatteryChargingState.Charging, reading.ChargingState.Value);
        Assert.Equal(45000.0, reading.ChargeRateMilliwatts.Value);
        Assert.Equal(45.0, reading.RateInWatts);
        Assert.Equal(AcPowerStatus.Online, reading.PowerStatus.Value);
    }

    [Fact]
    public async Task ReadTelemetryAsync_CriticalState_IdentifiedCorrectly()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.SimulatedStatus = mockApi.SimulatedStatus with
        {
            PowerState = BatteryNativeConstants.BATTERY_DISCHARGING | BatteryNativeConstants.BATTERY_CRITICAL,
            Capacity = 2000
        };
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.Equal(BatteryChargingState.Critical, reading.ChargingState.Value);
    }

    [Fact]
    public async Task ReadTelemetryAsync_RelativeCapacityBattery_CalculatesPercentageCorrectly()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.SimulatedInfo = mockApi.SimulatedInfo with
        {
            Capabilities = BatteryNativeConstants.BATTERY_SYSTEM_BATTERY | BatteryNativeConstants.BATTERY_CAPACITY_RELATIVE,
            FullChargedCapacity = 100
        };
        mockApi.SimulatedStatus = mockApi.SimulatedStatus with
        {
            Capacity = 85
        };
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.True(reading.BatteryPercentage.IsReported);
        Assert.Equal(85.0, reading.BatteryPercentage.Value);
    }

    [Fact]
    public async Task ReadTelemetryAsync_RelativeCapacity1000Scale_CalculatesPercentageCorrectly()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.SimulatedInfo = mockApi.SimulatedInfo with
        {
            Capabilities = BatteryNativeConstants.BATTERY_SYSTEM_BATTERY | BatteryNativeConstants.BATTERY_CAPACITY_RELATIVE,
            FullChargedCapacity = 1000 // Scale in tenths of a percent
        };
        mockApi.SimulatedStatus = mockApi.SimulatedStatus with
        {
            Capacity = 854 // 85.4%
        };
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.True(reading.BatteryPercentage.IsReported);
        Assert.Equal(85.4, reading.BatteryPercentage.Value);
    }

    [Fact]
    public async Task ReadTelemetryAsync_OnAcPower_AtChargeThreshold_ReportsNotCharging()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        // Plugged into AC, but not charging (e.g. held at 80% charge threshold)
        mockApi.SimulatedStatus = mockApi.SimulatedStatus with
        {
            PowerState = BatteryNativeConstants.BATTERY_POWER_ON_LINE,
            Capacity = 53200, // ~80% of 66500
            Rate = 0
        };
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.Equal(AcPowerStatus.Online, reading.PowerStatus.Value);
        Assert.Equal(BatteryChargingState.NotCharging, reading.ChargingState.Value);
    }

    [Fact]
    public async Task ReadTelemetryAsync_UnknownSentinels_HandledAsUnavailable()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.SimulatedInfo = mockApi.SimulatedInfo with
        {
            DesignedCapacity = BatteryNativeConstants.BATTERY_UNKNOWN_CAPACITY,
            FullChargedCapacity = BatteryNativeConstants.BATTERY_UNKNOWN_CAPACITY,
            CycleCount = 0
        };
        mockApi.SimulatedStatus = mockApi.SimulatedStatus with
        {
            Capacity = BatteryNativeConstants.BATTERY_UNKNOWN_CAPACITY,
            Voltage = BatteryNativeConstants.BATTERY_UNKNOWN_VOLTAGE,
            Rate = BatteryNativeConstants.BATTERY_UNKNOWN_RATE
        };
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.True(reading.DesignCapacityMwh.IsUnavailable);
        Assert.True(reading.FullChargeCapacityMwh.IsUnavailable);
        Assert.True(reading.RemainingCapacityMwh.IsUnavailable);
        Assert.True(reading.BatteryPercentage.IsUnavailable);
        Assert.True(reading.VoltageMillivolts.IsUnavailable);
        Assert.True(reading.ChargeRateMilliwatts.IsUnavailable);
        Assert.True(reading.CycleCount.IsUnavailable);
    }

    [Fact]
    public async Task ReadTelemetryAsync_BatteryTagChanged_DetectsReinitializationWithoutFalsePhysicalReplacement()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.NextBatteryTag = 100;
        mockApi.SimulatedSerialNumber = "SN-VICTUS-1";
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var firstReading = await reader.ReadTelemetryAsync();
        Assert.Equal(100u, reader.LastKnownBatteryTag);
        Assert.False(reader.WasTagReinitialized);
        Assert.False(reader.HasHardwareIdentityChanged);

        // Simulate driver/ACPI session re-initialization (tag increments, but serial number is identical)
        mockApi.NextBatteryTag = 101;
        var secondReading = await reader.ReadTelemetryAsync();

        Assert.Equal(101u, reader.LastKnownBatteryTag);
        Assert.True(reader.WasTagReinitialized);
        // Serial number did not change, so it is NOT a physical replacement
        Assert.False(reader.HasHardwareIdentityChanged);
        Assert.False(reader.WasBatteryReplaced);
    }

    [Fact]
    public async Task ReadTelemetryAsync_SerialNumberChanged_DetectsHardwareIdentityChange()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.NextBatteryTag = 100;
        mockApi.SimulatedSerialNumber = "SN-ORIGINAL-BATTERY";
        var reader = new WindowsIoctlBatteryReader(mockApi);

        await reader.ReadTelemetryAsync();
        Assert.False(reader.HasHardwareIdentityChanged);

        // Physical battery swap with different serial number
        mockApi.NextBatteryTag = 101;
        mockApi.SimulatedSerialNumber = "SN-REPLACEMENT-BATTERY";
        await reader.ReadTelemetryAsync();

        Assert.True(reader.WasTagReinitialized);
        Assert.True(reader.HasHardwareIdentityChanged);
        Assert.True(reader.WasBatteryReplaced);
    }

    [Fact]
    public async Task ReadTelemetryAsync_CleanHandleManagement_ReleasesDeviceHandle()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        var reader = new WindowsIoctlBatteryReader(mockApi);

        await reader.ReadTelemetryAsync();

        Assert.True(mockApi.OpenDeviceCallCount > 0);
        Assert.Equal(mockApi.OpenDeviceCallCount, mockApi.ReleaseHandleCallCount);
    }

    [Fact]
    public async Task ReadTelemetryAsync_NoBatteryDevices_ReturnsUnavailableReading()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.DevicePaths.Clear();
        var reader = new WindowsIoctlBatteryReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.Equal(SensorCapabilities.None, reading.AvailableSensors);
        Assert.True(reading.BatteryPercentage.IsUnavailable);
        Assert.Contains("No battery device interfaces discovered", reading.TelemetryProviderSource);
    }

    [Fact]
    public async Task ReadTelemetryAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        var reader = new WindowsIoctlBatteryReader(mockApi);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => reader.ReadTelemetryAsync(cts.Token));
    }
}
