using System;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Models;
using VictusGuardian.Core.Simulation;
using VictusGuardian.Windows.Power;
using VictusGuardian.Windows.Telemetry;

namespace VictusGuardian.Windows.Composite;

public class CompositeBatteryTelemetryProvider : ICompositeBatteryTelemetryProvider, IDisposable
{
    private readonly IWindowsIoctlBatteryReader _ioctlReader;
    private readonly IWindowsPowerStatusReader _powerStatusReader;
    private readonly SimulatedBatteryTelemetryProvider _simulatedProvider;
    private readonly INativePowerEvents? _powerEvents;

    private bool _isDemoModeEnabled = false;
    private DateTimeOffset? _lastSuccessfulReadingTime;
    private string _activeProviderTier = "Initializing";
    private bool _isDisposed;

    public string ProviderName => "Composite Windows Battery Telemetry";
    public bool IsHardwareSupported => _ioctlReader.IsSupported || _powerStatusReader.IsSupported;

    public bool IsDemoModeEnabled
    {
        get => _isDemoModeEnabled;
        set
        {
            _isDemoModeEnabled = value;
            if (value)
            {
                _activeProviderTier = "Demo Mode (Simulated)";
            }
        }
    }

    public string ActiveProviderTier => _activeProviderTier;
    public DateTimeOffset? LastSuccessfulReadingTime => _lastSuccessfulReadingTime;

    public TimeSpan StalenessThreshold { get; set; } = TimeSpan.FromSeconds(20);

    public bool IsReadingStale =>
        _lastSuccessfulReadingTime.HasValue &&
        (DateTimeOffset.UtcNow - _lastSuccessfulReadingTime.Value) > StalenessThreshold;

    public CompositeBatteryTelemetryProvider(
        IWindowsIoctlBatteryReader ioctlReader,
        IWindowsPowerStatusReader powerStatusReader,
        SimulatedBatteryTelemetryProvider simulatedProvider,
        INativePowerEvents? powerEvents = null)
    {
        _ioctlReader = ioctlReader;
        _powerStatusReader = powerStatusReader;
        _simulatedProvider = simulatedProvider;
        _powerEvents = powerEvents;

        if (_powerEvents != null)
        {
            _powerEvents.PowerStateChanged += OnPowerStateChanged;
        }
    }

    private void OnPowerStateChanged(PowerStateChangeType changeType)
    {
        switch (changeType)
        {
            case PowerStateChangeType.Suspend:
                _ioctlReader.InvalidateTagCache();
                break;
            case PowerStateChangeType.Resume:
                _ioctlReader.InvalidateTagCache();
                break;
            case PowerStateChangeType.StatusChanged:
                break;
        }
    }

    public async Task<BatteryReading> GetReadingAsync(CancellationToken cancellationToken = default)
    {
        // 1. Explicit Demo Mode selection ONLY
        if (_isDemoModeEnabled)
        {
            _activeProviderTier = "Demo Mode (Simulated)";
            var simReading = await _simulatedProvider.GetReadingAsync(cancellationToken);
            return simReading with
            {
                TelemetryProviderSource = "Demo Mode (Simulated)"
            };
        }

        // 2. Preferred: Windows Battery Interface Queries (IOCTL)
        if (_ioctlReader.IsSupported)
        {
            try
            {
                var ioctlReading = await _ioctlReader.ReadTelemetryAsync(cancellationToken);
                if (ioctlReading.AvailableSensors != SensorCapabilities.None)
                {
                    _lastSuccessfulReadingTime = DateTimeOffset.UtcNow;
                    _activeProviderTier = "Windows IOCTL (Direct Device Interface)";
                    return ioctlReading with
                    {
                        IsStale = false,
                        TelemetryProviderSource = _activeProviderTier
                    };
                }
            }
            catch
            {
                // Fall through to fallback
            }
        }

        // 3. Fallback: GetSystemPowerStatus
        if (_powerStatusReader.IsSupported)
        {
            try
            {
                var powerReading = await _powerStatusReader.ReadTelemetryAsync(cancellationToken);
                if (powerReading.AvailableSensors != SensorCapabilities.None)
                {
                    _lastSuccessfulReadingTime = DateTimeOffset.UtcNow;
                    _activeProviderTier = "Windows Win32 (GetSystemPowerStatus Fallback)";
                    return powerReading with
                    {
                        IsStale = false,
                        TelemetryProviderSource = _activeProviderTier
                    };
                }
            }
            catch
            {
                // Fall through to unavailable
            }
        }

        // 4. Terminal State: Clear Telemetry-Unavailable (Never silently switch to simulated data!)
        _activeProviderTier = "Telemetry Unavailable";
        return CreateUnavailableReading("Telemetry Unavailable: Physical battery sensors could not be read on this system.");
    }

    private BatteryReading CreateUnavailableReading(string reason)
    {
        return new BatteryReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            AvailableSensors = SensorCapabilities.None,
            BatteryPercentage = TelemetryValue<double>.Unavailable,
            PowerStatus = TelemetryValue<AcPowerStatus>.Unavailable,
            ChargingState = TelemetryValue<BatteryChargingState>.Unavailable,
            RemainingCapacityMwh = TelemetryValue<double>.Unavailable,
            FullChargeCapacityMwh = TelemetryValue<double>.Unavailable,
            DesignCapacityMwh = TelemetryValue<double>.Unavailable,
            ChargeRateMilliwatts = TelemetryValue<double>.Unavailable,
            CycleCount = TelemetryValue<int>.Unavailable,
            VoltageMillivolts = TelemetryValue<double>.Unavailable,
            BatteryName = TelemetryValue<string>.Unavailable,
            Manufacturer = TelemetryValue<string>.Unavailable,
            SerialNumber = TelemetryValue<string>.Unavailable,
            Chemistry = TelemetryValue<string>.Unavailable,
            IsStale = IsReadingStale,
            TelemetryProviderSource = reason
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_powerEvents != null)
        {
            _powerEvents.PowerStateChanged -= OnPowerStateChanged;
            _powerEvents.Dispose();
        }
    }
}
