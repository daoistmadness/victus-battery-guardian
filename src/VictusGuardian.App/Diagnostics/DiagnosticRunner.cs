using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Models;
using VictusGuardian.Core.Simulation;
using VictusGuardian.Windows.Composite;
using VictusGuardian.Windows.Native;
using VictusGuardian.Windows.Power;
using VictusGuardian.Windows.Telemetry;

namespace VictusGuardian.App.Diagnostics;

public static class DiagnosticRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var outputJson = false;
        string? exportFilePath = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                outputJson = true;
            }
            else if (args[i].Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                exportFilePath = args[++i];
            }
        }

        var nativeApi = OperatingSystem.IsWindows()
            ? (INativeBatteryDeviceApi)new WindowsNativeBatteryDeviceApi()
            : new UnsupportedNativeBatteryDeviceApi();

        var ioctlReader = new WindowsIoctlBatteryReader(nativeApi);
        var powerReader = new WindowsPowerStatusReader(nativeApi);
        var simProvider = new SimulatedBatteryTelemetryProvider();
        var powerEvents = new WindowsPowerEventNotifier();
        using var composite = new CompositeBatteryTelemetryProvider(ioctlReader, powerReader, simProvider, powerEvents);

        var version = AppVersion.FullVersion;
        var commitSha = AppVersion.CommitSha;
        var renderMode = Program.GetEffectiveRenderMode(args);
        var renderingMode = renderMode == Program.RenderMode.Hardware
            ? "Hardware Accelerated (Win32RenderingMode.AngleEgl / Direct3D)"
            : "Software Rendering (Win32RenderingMode.Software, RedirectionSurface) [Default]";

        var reading = await composite.GetReadingAsync();

        // Anonymize serial number for privacy
        var anonymizedSerial = reading.SerialNumber.HasValue && !string.IsNullOrWhiteSpace(reading.SerialNumber.Value)
            ? (reading.SerialNumber.Value.Length > 4
                ? $"****{reading.SerialNumber.Value[^4..]}"
                : "****")
            : "Unavailable";

        var supportedSensors = GetSensorList(reading.AvailableSensors);
        var allSensors = SensorCapabilities.All;
        var unsupportedSensors = GetSensorList(allSensors & ~reading.AvailableSensors);

        var report = new DiagnosticReport
        {
            Application = "Victus Battery Guardian",
            Version = version,
            CommitSha = commitSha,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            TimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
            ActiveProviderTier = composite.ActiveProviderTier,
            RenderingMode = renderingMode,
            IsDemoMode = composite.IsDemoModeEnabled,
            IsHardwareSupported = composite.IsHardwareSupported,
            IsStale = reading.IsStale,
            TelemetrySource = reading.TelemetryProviderSource,
            Metrics = new DiagnosticMetrics
            {
                BatteryPercentage = FormatMetric(reading.BatteryPercentage, "%"),
                AcPowerStatus = FormatMetric(reading.PowerStatus),
                ChargingState = FormatMetric(reading.ChargingState),
                RemainingCapacityMwh = FormatMetric(reading.RemainingCapacityMwh, "mWh"),
                FullChargeCapacityMwh = FormatMetric(reading.FullChargeCapacityMwh, "mWh"),
                DesignCapacityMwh = FormatMetric(reading.DesignCapacityMwh, "mWh"),
                ChargeRateWatts = FormatMetric(reading.ChargeRateMilliwatts.HasValue
                    ? TelemetryValue<double>.Reported(reading.ChargeRateMilliwatts.Value / 1000.0)
                    : TelemetryValue<double>.Unavailable, "W"),
                VoltageVolts = FormatMetric(reading.VoltageMillivolts.HasValue
                    ? TelemetryValue<double>.Reported(reading.VoltageMillivolts.Value / 1000.0)
                    : TelemetryValue<double>.Unavailable, "V"),
                CycleCount = FormatMetric(reading.CycleCount, "cycles"),
                BatteryDeviceName = FormatMetric(reading.BatteryName),
                Manufacturer = FormatMetric(reading.Manufacturer),
                Chemistry = FormatMetric(reading.Chemistry),
                SerialNumber = new DiagnosticValue { Value = anonymizedSerial, Provenance = "Anonymized" }
            },
            SensorChannels = new SensorChannelInfo
            {
                AvailableMask = (uint)reading.AvailableSensors,
                Supported = supportedSensors,
                Unsupported = unsupportedSensors
            }
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        if (!string.IsNullOrEmpty(exportFilePath))
        {
            await File.WriteAllTextAsync(exportFilePath, json, Encoding.UTF8);
            Console.WriteLine($"[DIAGNOSTICS] Report written to: {exportFilePath}");
        }

        if (outputJson)
        {
            Console.WriteLine(json);
        }
        else
        {
            PrintHumanReadableReport(report);
        }

        return 0;
    }

    private static DiagnosticValue FormatMetric<T>(TelemetryValue<T> val, string unit = "")
    {
        return new DiagnosticValue
        {
            Value = val.HasValue ? $"{val.Value} {unit}".Trim() : "Unavailable",
            Provenance = val.Source.ToString()
        };
    }

    private static List<string> GetSensorList(SensorCapabilities caps)
    {
        var list = new List<string>();
        foreach (SensorCapabilities flag in Enum.GetValues<SensorCapabilities>())
        {
            if (flag != SensorCapabilities.None && flag != SensorCapabilities.All && (caps & flag) != 0)
            {
                list.Add(flag.ToString());
            }
        }
        return list;
    }

    private static void PrintHumanReadableReport(DiagnosticReport r)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine($" VICTUS BATTERY GUARDIAN - HARDWARE DIAGNOSTIC REPORT");
        Console.WriteLine("================================================================================");
        Console.WriteLine($" App Version:      {r.Version} (Commit: {r.CommitSha})");
        Console.WriteLine($" OS:               {r.OperatingSystem} ({r.Architecture})");
        Console.WriteLine($" Timestamp (UTC):  {r.TimestampUtc}");
        Console.WriteLine($" Telemetry Tier:   {r.ActiveProviderTier}");
        Console.WriteLine($" Rendering Backend: {r.RenderingMode}");
        Console.WriteLine($" Telemetry Source: {r.TelemetrySource}");
        Console.WriteLine($" Demo Mode:        {(r.IsDemoMode ? "ENABLED (Simulated)" : "DISABLED (Live Hardware)")}");
        Console.WriteLine($" Data Staleness:   {(r.IsStale ? "WARNING: Reading is stale" : "Fresh")}");
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine(" METRIC                           VALUE                  PROVENANCE");
        Console.WriteLine("--------------------------------------------------------------------------------");
        PrintRow("Battery Percentage", r.Metrics.BatteryPercentage);
        PrintRow("AC Line Status", r.Metrics.AcPowerStatus);
        PrintRow("Charging State", r.Metrics.ChargingState);
        PrintRow("Remaining Capacity", r.Metrics.RemainingCapacityMwh);
        PrintRow("Full Charge Capacity", r.Metrics.FullChargeCapacityMwh);
        PrintRow("Design Capacity", r.Metrics.DesignCapacityMwh);
        PrintRow("Charge/Discharge Rate", r.Metrics.ChargeRateWatts);
        PrintRow("Battery Voltage", r.Metrics.VoltageVolts);
        PrintRow("Cycle Count", r.Metrics.CycleCount);
        PrintRow("Battery Name", r.Metrics.BatteryDeviceName);
        PrintRow("Manufacturer", r.Metrics.Manufacturer);
        PrintRow("Chemistry", r.Metrics.Chemistry);
        PrintRow("Serial Number (Anonymized)", r.Metrics.SerialNumber);
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine($" Supported Channels:   {string.Join(", ", r.SensorChannels.Supported)}");
        Console.WriteLine($" Unsupported Channels: {string.Join(", ", r.SensorChannels.Unsupported)}");
        Console.WriteLine("================================================================================");
    }

    private static void PrintRow(string name, DiagnosticValue v)
    {
        Console.WriteLine($" {name,-32} {v.Value,-22} [{v.Provenance}]");
    }
}

public class DiagnosticReport
{
    public string Application { get; set; } = "";
    public string Version { get; set; } = "";
    public string CommitSha { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
    public string ActiveProviderTier { get; set; } = "";
    public string RenderingMode { get; set; } = "";
    public bool IsDemoMode { get; set; }
    public bool IsHardwareSupported { get; set; }
    public bool IsStale { get; set; }
    public string TelemetrySource { get; set; } = "";
    public DiagnosticMetrics Metrics { get; set; } = new();
    public SensorChannelInfo SensorChannels { get; set; } = new();
}

public class DiagnosticMetrics
{
    public DiagnosticValue BatteryPercentage { get; set; } = new();
    public DiagnosticValue AcPowerStatus { get; set; } = new();
    public DiagnosticValue ChargingState { get; set; } = new();
    public DiagnosticValue RemainingCapacityMwh { get; set; } = new();
    public DiagnosticValue FullChargeCapacityMwh { get; set; } = new();
    public DiagnosticValue DesignCapacityMwh { get; set; } = new();
    public DiagnosticValue ChargeRateWatts { get; set; } = new();
    public DiagnosticValue VoltageVolts { get; set; } = new();
    public DiagnosticValue CycleCount { get; set; } = new();
    public DiagnosticValue BatteryDeviceName { get; set; } = new();
    public DiagnosticValue Manufacturer { get; set; } = new();
    public DiagnosticValue Chemistry { get; set; } = new();
    public DiagnosticValue SerialNumber { get; set; } = new();
}

public class DiagnosticValue
{
    public string Value { get; set; } = "";
    public string Provenance { get; set; } = "";
}

public class SensorChannelInfo
{
    public uint AvailableMask { get; set; }
    public List<string> Supported { get; set; } = new();
    public List<string> Unsupported { get; set; } = new();
}
