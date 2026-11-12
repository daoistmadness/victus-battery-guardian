using System;
using VictusGuardian.Core.Models;

namespace VictusGuardian.Core.Contracts;

/// <summary>
/// Composite telemetry provider coordinating multiple provider tiers:
/// Preferred: Windows IOCTL direct battery interface
/// Fallback: Windows Win32 GetSystemPowerStatus
/// Terminal: Explicit unavailable reading (never silent simulation)
/// Demo Mode: Explicit user-activated simulation only.
/// </summary>
public interface ICompositeBatteryTelemetryProvider : IBatteryTelemetryProvider
{
    /// <summary>
    /// Explicit toggle for Demo Mode.
    /// Hardware telemetry is NEVER silently substituted with simulated data.
    /// </summary>
    bool IsDemoModeEnabled { get; set; }

    /// <summary>
    /// Current active telemetry provider tier (e.g., "Windows IOCTL (Direct Device Interface)", "Windows Win32 (GetSystemPowerStatus Fallback)", "Unavailable", or "Demo Mode (Simulated)").
    /// </summary>
    string ActiveProviderTier { get; }

    /// <summary>
    /// Timestamp of the last successful hardware telemetry read.
    /// Used for staleness detection and warning badges.
    /// </summary>
    DateTimeOffset? LastSuccessfulReadingTime { get; }

    /// <summary>
    /// True when the current reading is considered stale (e.g., elapsed time since last successful reading exceeds threshold).
    /// </summary>
    bool IsReadingStale { get; }
}
