using System.Threading.Tasks;
using VictusGuardian.Core.Models;
using VictusGuardian.Windows.Native;
using VictusGuardian.Windows.Telemetry;
using Xunit;

namespace VictusGuardian.UnitTests;

public class WindowsPowerStatusReaderTests
{
    [Fact]
    public async Task ReadTelemetryAsync_OnlineAc_ReturnsReportedStatus()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.SimulatedPowerStatus = new NativeSystemPowerStatus
        {
            ACLineStatus = 1,
            BatteryLifePercent = 95,
            BatteryFlag = 8 // Charging
        };
        var reader = new WindowsPowerStatusReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.True(reading.BatteryPercentage.IsReported);
        Assert.Equal(95.0, reading.BatteryPercentage.Value);
        Assert.Equal(AcPowerStatus.Online, reading.PowerStatus.Value);
        Assert.Equal(BatteryChargingState.Charging, reading.ChargingState.Value);
        Assert.Contains("GetSystemPowerStatus Fallback", reading.TelemetryProviderSource);
    }

    [Fact]
    public async Task ReadTelemetryAsync_OfflineAc_ReturnsDischargingStatus()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.SimulatedPowerStatus = new NativeSystemPowerStatus
        {
            ACLineStatus = 0,
            BatteryLifePercent = 45,
            BatteryFlag = 0
        };
        var reader = new WindowsPowerStatusReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.Equal(45.0, reading.BatteryPercentage.Value);
        Assert.Equal(AcPowerStatus.Offline, reading.PowerStatus.Value);
        Assert.Equal(BatteryChargingState.Discharging, reading.ChargingState.Value);
    }

    [Fact]
    public async Task ReadTelemetryAsync_InvalidPercentage255_ReportsUnavailable()
    {
        var mockApi = new MockNativeBatteryDeviceApi();
        mockApi.SimulatedPowerStatus = new NativeSystemPowerStatus
        {
            ACLineStatus = 1,
            BatteryLifePercent = 255, // 255 = battery status unknown in Win32
            BatteryFlag = 128 // No system battery
        };
        var reader = new WindowsPowerStatusReader(mockApi);

        var reading = await reader.ReadTelemetryAsync();

        Assert.True(reading.BatteryPercentage.IsUnavailable);
    }
}
