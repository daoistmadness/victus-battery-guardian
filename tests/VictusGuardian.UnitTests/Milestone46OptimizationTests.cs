using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using VictusGuardian.App;
using VictusGuardian.App.ViewModels;
using VictusGuardian.Core;
using VictusGuardian.Core.Alerts;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Health;
using VictusGuardian.Core.Models;
using VictusGuardian.Storage.Models;
using VictusGuardian.Storage.Repositories;
using Xunit;

namespace VictusGuardian.UnitTests;

public class Milestone46OptimizationTests
{
    [Theory]
    [InlineData(new[] { "--software-rendering" }, true)]
    [InlineData(new[] { "--software" }, true)]
    [InlineData(new[] { "--render-mode=software" }, true)]
    [InlineData(new[] { "--render-mode", "software" }, true)]
    [InlineData(new[] { "--other-flag" }, false)]
    [InlineData(new string[0], false)]
    public void IsSoftwareRenderingRequested_CommandLineArguments_EvaluatedCorrectly(string[] args, bool expected)
    {
        // Clear environment variables to isolate CLI argument testing
        var origRender = Environment.GetEnvironmentVariable("VICTUS_RENDER_MODE");
        var origSoftware = Environment.GetEnvironmentVariable("VICTUS_SOFTWARE_RENDERING");
        try
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", null);
            Environment.SetEnvironmentVariable("VICTUS_SOFTWARE_RENDERING", null);

            var actual = Program.IsSoftwareRenderingRequested(args);
            Assert.Equal(expected, actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", origRender);
            Environment.SetEnvironmentVariable("VICTUS_SOFTWARE_RENDERING", origSoftware);
        }
    }

    [Theory]
    [InlineData(new[] { "--hardware-rendering" }, true)]
    [InlineData(new[] { "--hardware" }, true)]
    [InlineData(new[] { "--render-mode=hardware" }, true)]
    [InlineData(new[] { "--render-mode", "hardware" }, true)]
    [InlineData(new[] { "--other-flag" }, false)]
    [InlineData(new string[0], false)]
    public void IsHardwareRenderingRequested_CommandLineArguments_EvaluatedCorrectly(string[] args, bool expected)
    {
        var origRender = Environment.GetEnvironmentVariable("VICTUS_RENDER_MODE");
        var origHardware = Environment.GetEnvironmentVariable("VICTUS_HARDWARE_RENDERING");
        try
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", null);
            Environment.SetEnvironmentVariable("VICTUS_HARDWARE_RENDERING", null);

            var actual = Program.IsHardwareRenderingRequested(args);
            Assert.Equal(expected, actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", origRender);
            Environment.SetEnvironmentVariable("VICTUS_HARDWARE_RENDERING", origHardware);
        }
    }

    [Fact]
    public void IsHardwareRenderingRequested_EnvironmentVariable_EvaluatedCorrectly()
    {
        var origRender = Environment.GetEnvironmentVariable("VICTUS_RENDER_MODE");
        var origHardware = Environment.GetEnvironmentVariable("VICTUS_HARDWARE_RENDERING");
        try
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", "hardware");
            Assert.True(Program.IsHardwareRenderingRequested(Array.Empty<string>()));

            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", "software");
            Assert.False(Program.IsHardwareRenderingRequested(Array.Empty<string>()));

            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", null);
            Environment.SetEnvironmentVariable("VICTUS_HARDWARE_RENDERING", "1");
            Assert.True(Program.IsHardwareRenderingRequested(Array.Empty<string>()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", origRender);
            Environment.SetEnvironmentVariable("VICTUS_HARDWARE_RENDERING", origHardware);
        }
    }

    [Fact]
    public void GetEffectiveRenderMode_DefaultsToSoftware_UnlessHardwareRequested()
    {
        var origRender = Environment.GetEnvironmentVariable("VICTUS_RENDER_MODE");
        var origHardware = Environment.GetEnvironmentVariable("VICTUS_HARDWARE_RENDERING");
        try
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", null);
            Environment.SetEnvironmentVariable("VICTUS_HARDWARE_RENDERING", null);

            // Default on Windows is Software
            Assert.Equal(Program.RenderMode.Software, Program.GetEffectiveRenderMode(Array.Empty<string>()));

            // Explicit software request remains Software
            Assert.Equal(Program.RenderMode.Software, Program.GetEffectiveRenderMode(new[] { "--software-rendering" }));

            // Explicit hardware request activates Hardware
            Assert.Equal(Program.RenderMode.Hardware, Program.GetEffectiveRenderMode(new[] { "--hardware-rendering" }));
            Assert.Equal(Program.RenderMode.Hardware, Program.GetEffectiveRenderMode(new[] { "--hardware" }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", origRender);
            Environment.SetEnvironmentVariable("VICTUS_HARDWARE_RENDERING", origHardware);
        }
    }

    [Fact]
    public void IsSoftwareRenderingRequested_EnvironmentVariable_EvaluatedCorrectly()
    {
        var origRender = Environment.GetEnvironmentVariable("VICTUS_RENDER_MODE");
        try
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", "software");
            Assert.True(Program.IsSoftwareRenderingRequested(Array.Empty<string>()));

            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", "hardware");
            Assert.False(Program.IsSoftwareRenderingRequested(Array.Empty<string>()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VICTUS_RENDER_MODE", origRender);
        }
    }

    [Fact]
    public void BatteryReadingRecord_FormattedVoltage_FormatsVoltsConsistently()
    {
        var readingWithMv = new BatteryReadingRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Percentage = 85.0,
            IsAcConnected = true,
            ChargingState = "Charging",
            VoltageMillivolts = 17640.0,
            IsSimulated = false
        };

        Assert.Equal("17.64 V", readingWithMv.FormattedVoltage);
        Assert.Equal(17.64, readingWithMv.VoltageVolts);

        var readingWithoutMv = new BatteryReadingRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Percentage = 85.0,
            IsAcConnected = true,
            ChargingState = "Charging",
            VoltageMillivolts = null,
            IsSimulated = false
        };

        Assert.Equal("—", readingWithoutMv.FormattedVoltage);
        Assert.Null(readingWithoutMv.VoltageVolts);
    }

    [Fact]
    public void ChargingSessionRecord_ActiveSession_DoesNotProduceMalformedStrings()
    {
        var activeSession = new ChargingSessionRecord
        {
            Id = 1,
            StartTime = DateTimeOffset.UtcNow,
            EndTime = null,
            StartPercentage = 13.0,
            EndPercentage = null,
            DurationMinutes = null,
            EnergyGainedMwh = null,
            IsCompleted = false
        };

        Assert.Equal("From 13%", activeSession.FormattedStartPercentage);
        Assert.Equal("(Active)", activeSession.FormattedEndPercentage);
        Assert.Equal("In Progress", activeSession.FormattedDuration);
        Assert.Equal("—", activeSession.FormattedEnergyGained);
        Assert.Equal("Active", activeSession.StatusBadge);

        // Verify completed session formats fully
        var completedSession = activeSession with
        {
            EndTime = activeSession.StartTime.AddMinutes(45),
            EndPercentage = 80.0,
            DurationMinutes = 45.0,
            EnergyGainedMwh = 15200.0,
            IsCompleted = true
        };

        Assert.Equal("To 80%", completedSession.FormattedEndPercentage);
        Assert.Equal("45 min", completedSession.FormattedDuration);
        Assert.Equal("+15,200 mWh", completedSession.FormattedEnergyGained);
        Assert.Equal("Completed", completedSession.StatusBadge);
    }

    [Fact]
    public async Task SqliteBatteryHistoryRepository_WriteSuppression_SuppressesStaticReadings()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"victus_test_throttle_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={tempDb};";

        try
        {
            var repo = new SqliteBatteryHistoryRepository(connectionString);
            await repo.InitializeAsync();

            var baseTime = DateTimeOffset.UtcNow;
            var reading1 = new BatteryReading
            {
                Timestamp = baseTime,
                AvailableSensors = SensorCapabilities.All,
                BatteryPercentage = TelemetryValue<double>.Reported(80.0),
                PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
                ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.NotCharging),
                VoltageMillivolts = TelemetryValue<double>.Reported(17400.0),
                TelemetryProviderSource = "Windows IOCTL"
            };

            // First write must succeed
            var wrote1 = await repo.RecordReadingAsync(reading1);
            Assert.True(wrote1);

            // Identical reading 30 seconds later must be suppressed
            var reading2 = reading1 with
            {
                Timestamp = baseTime.AddSeconds(30)
            };
            var wrote2 = await repo.RecordReadingAsync(reading2);
            Assert.False(wrote2, "Static reading within 5 minutes must be suppressed to reduce SQLite write amplification.");

            // Reading with significant voltage jump (> 250 mV) must be recorded
            var readingVoltageJump = reading1 with
            {
                Timestamp = baseTime.AddSeconds(60),
                VoltageMillivolts = TelemetryValue<double>.Reported(17800.0) // 400 mV delta
            };
            var wroteVoltageJump = await repo.RecordReadingAsync(readingVoltageJump);
            Assert.True(wroteVoltageJump, "Significant voltage transient (> 250 mV) must be recorded.");

            // Reading with state change (e.g. AC toggle) must be recorded
            var readingAcToggle = readingVoltageJump with
            {
                Timestamp = baseTime.AddSeconds(90),
                PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
                ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging)
            };
            var wroteAcToggle = await repo.RecordReadingAsync(readingAcToggle);
            Assert.True(wroteAcToggle, "AC / charging state toggle must be recorded immediately.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(tempDb)) File.Delete(tempDb);
            }
            catch { }
        }
    }

    [Fact]
    public void AlertRules_HighChargeReminder_FiresOnAcRegardlessOfReportedChargingSubstate()
    {
        var evaluator = new DefaultBatteryAlertEvaluator();
        var settings = new AlertRuleSettings
        {
            NotifyAtMaxChargeTarget = true,
            MaxChargeTargetPercentage = 80.0,
            HysteresisPercentage = 2.0,
            AlertCooldown = TimeSpan.FromMinutes(5)
        };

        var now = DateTimeOffset.UtcNow;

        // Condition 1: 98% on AC with ChargingState = Full
        var readingFull = new BatteryReading
        {
            Timestamp = now,
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(98.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Full),
            TelemetryProviderSource = "Windows IOCTL"
        };

        var alertsFull = evaluator.Evaluate(readingFull, null, settings);
        Assert.Contains(alertsFull, a => a.Category == BatteryAlertCategory.HighChargeReminder);

        // Condition 2: Cooldown suppresses immediate duplicate
        var readingFullSecond = readingFull with { Timestamp = now.AddSeconds(10) };
        var alertsCooldown = evaluator.Evaluate(readingFullSecond, null, settings);
        Assert.DoesNotContain(alertsCooldown, a => a.Category == BatteryAlertCategory.HighChargeReminder);

        // Condition 3: Disconnecting AC re-arms evaluator
        var readingOnBattery = new BatteryReading
        {
            Timestamp = now.AddMinutes(1),
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(97.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Offline),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.Discharging),
            TelemetryProviderSource = "Windows IOCTL"
        };
        evaluator.Evaluate(readingOnBattery, null, settings);

        // Condition 4: Plugging back in on AC fires reminder again after cooldown
        var readingPluggedBack = new BatteryReading
        {
            Timestamp = now.AddMinutes(6),
            AvailableSensors = SensorCapabilities.All,
            BatteryPercentage = TelemetryValue<double>.Reported(97.0),
            PowerStatus = TelemetryValue<AcPowerStatus>.Reported(AcPowerStatus.Online),
            ChargingState = TelemetryValue<BatteryChargingState>.Reported(BatteryChargingState.NotCharging),
            TelemetryProviderSource = "Windows IOCTL"
        };
        var alertsRearmed = evaluator.Evaluate(readingPluggedBack, null, settings);
        Assert.Contains(alertsRearmed, a => a.Category == BatteryAlertCategory.HighChargeReminder);
    }
}
