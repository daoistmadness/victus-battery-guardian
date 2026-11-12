using System;

namespace VictusGuardian.Core.Models;

/// <summary>
/// Identifies the provenance of a telemetry reading.
/// </summary>
public enum TelemetrySource
{
    /// <summary>Direct reading from physical hardware sensors.</summary>
    Reported = 1,

    /// <summary>Derived or estimated from historical readings.</summary>
    Estimated = 2,

    /// <summary>Sensor reading is unavailable on this device or platform.</summary>
    Unavailable = 3,

    /// <summary>Simulated reading generated for demo or testing purposes. Never presented as real telemetry.</summary>
    Simulated = 4
}

/// <summary>
/// Status of AC mains power connection.
/// </summary>
public enum AcPowerStatus
{
    Unknown = 0,
    Online = 1,
    Offline = 2
}

/// <summary>
/// Operational charging/discharging state of the battery.
/// </summary>
public enum BatteryChargingState
{
    Unknown = 0,
    Discharging = 1,
    Charging = 2,
    Full = 3,
    Critical = 4,
    NotCharging = 5
}

/// <summary>
/// Identifies which sensors and telemetry fields are supported by the provider.
/// </summary>
[Flags]
public enum SensorCapabilities
{
    None = 0,
    Percentage = 1 << 0,
    PowerStatus = 1 << 1,
    ChargingState = 1 << 2,
    RemainingCapacity = 1 << 3,
    FullChargeCapacity = 1 << 4,
    DesignCapacity = 1 << 5,
    ChargeRate = 1 << 6,
    CycleCount = 1 << 7,
    All = Percentage | PowerStatus | ChargingState | RemainingCapacity | FullChargeCapacity | DesignCapacity | ChargeRate | CycleCount
}

/// <summary>
/// Strongly-typed wrapper that couples a telemetry value with its provenance.
/// Guarantees that simulated, estimated, and unavailable values are strictly distinguishable from real hardware readings.
/// </summary>
public readonly record struct TelemetryValue<T>(T? Value, TelemetrySource Source)
{
    public bool HasValue => Source != TelemetrySource.Unavailable && Value is not null;
    public bool IsReported => Source == TelemetrySource.Reported;
    public bool IsEstimated => Source == TelemetrySource.Estimated;
    public bool IsUnavailable => Source == TelemetrySource.Unavailable;
    public bool IsSimulated => Source == TelemetrySource.Simulated;

    public static TelemetryValue<T> Reported(T value) => new(value, TelemetrySource.Reported);
    public static TelemetryValue<T> Estimated(T value) => new(value, TelemetrySource.Estimated);
    public static TelemetryValue<T> Simulated(T value) => new(value, TelemetrySource.Simulated);
    public static TelemetryValue<T> Unavailable => new(default, TelemetrySource.Unavailable);

    public override string ToString() => Source switch
    {
        TelemetrySource.Unavailable => "N/A",
        TelemetrySource.Simulated => $"{Value} (Simulated)",
        TelemetrySource.Estimated => $"{Value} (Est)",
        _ => Value?.ToString() ?? "N/A"
    };
}

/// <summary>
/// Normalized snapshot of battery telemetry at a specific point in time.
/// </summary>
public record BatteryReading
{
    public required DateTimeOffset Timestamp { get; init; }
    public required SensorCapabilities AvailableSensors { get; init; }

    public TelemetryValue<double> BatteryPercentage { get; init; } = TelemetryValue<double>.Unavailable;
    public TelemetryValue<AcPowerStatus> PowerStatus { get; init; } = TelemetryValue<AcPowerStatus>.Unavailable;
    public TelemetryValue<BatteryChargingState> ChargingState { get; init; } = TelemetryValue<BatteryChargingState>.Unavailable;
    public TelemetryValue<double> RemainingCapacityMwh { get; init; } = TelemetryValue<double>.Unavailable;
    public TelemetryValue<double> FullChargeCapacityMwh { get; init; } = TelemetryValue<double>.Unavailable;
    public TelemetryValue<double> DesignCapacityMwh { get; init; } = TelemetryValue<double>.Unavailable;
    public TelemetryValue<double> ChargeRateMilliwatts { get; init; } = TelemetryValue<double>.Unavailable;
    public TelemetryValue<int> CycleCount { get; init; } = TelemetryValue<int>.Unavailable;

    /// <summary>True if any sensor reading is simulated (Demo Mode or testing).</summary>
    public bool IsSimulated => BatteryPercentage.IsSimulated ||
                               ChargingState.IsSimulated ||
                               PowerStatus.IsSimulated;

    /// <summary>True if AC power is connected.</summary>
    public bool IsAcConnected => PowerStatus.HasValue && PowerStatus.Value == AcPowerStatus.Online;

    /// <summary>Discharge or charge power in Watts (absolute value).</summary>
    public double? RateInWatts => ChargeRateMilliwatts.HasValue
        ? Math.Abs(ChargeRateMilliwatts.Value) / 1000.0
        : null;

    /// <summary>
    /// Estimated time remaining until full charge or complete discharge based on current rate and capacity.
    /// Returns null if rate is zero, unavailable, or idle.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            if (!ChargeRateMilliwatts.HasValue || Math.Abs(ChargeRateMilliwatts.Value) < 100)
                return null;

            var rateMwhPerHour = Math.Abs(ChargeRateMilliwatts.Value);

            if (ChargingState.Value == BatteryChargingState.Charging &&
                FullChargeCapacityMwh.HasValue &&
                RemainingCapacityMwh.HasValue)
            {
                var neededMwh = Math.Max(0, FullChargeCapacityMwh.Value - RemainingCapacityMwh.Value);
                var hours = neededMwh / rateMwhPerHour;
                return TimeSpan.FromHours(Math.Min(hours, 24));
            }

            if (ChargingState.Value == BatteryChargingState.Discharging &&
                RemainingCapacityMwh.HasValue)
            {
                var hours = RemainingCapacityMwh.Value / rateMwhPerHour;
                return TimeSpan.FromHours(Math.Min(hours, 48));
            }

            return null;
        }
    }
}
