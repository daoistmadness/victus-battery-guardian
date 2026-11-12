using System;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Core.Models;

namespace VictusGuardian.Core.Simulation;

public interface ISimulatedBatteryService : IBatteryTelemetryProvider
{
    void SetPercentage(double percentage);
    void SetAcConnected(bool connected);
    void SetDischargeRateWatts(double watts);
    void SetChargeRateWatts(double watts);
    void SetCycleCount(int cycles);
    void ResetToDefaultVictusProfile();
}

/// <summary>
/// Provides simulated battery telemetry for Demo Mode and cross-platform testing.
/// All emitted values explicitly carry TelemetrySource.Simulated provenance.
/// Emulates HP Victus 15.6 default battery characteristics (70 Wh design capacity).
/// </summary>
public class SimulatedBatteryTelemetryProvider : ISimulatedBatteryService
{
    private readonly object _sync = new();

    private double _percentage = 78.5;
    private bool _isAcConnected = false;
    private double _designCapacityMwh = 70070.0; // HP Victus 70 Wh battery
    private double _fullChargeCapacityMwh = 66500.0; // ~95% health
    private double _rateWatts = 18.5; // ~18.5W typical laptop discharge
    private int _cycleCount = 42;

    public string ProviderName => "HP Victus Demo Simulator (Simulated)";
    public bool IsHardwareSupported => true;

    public void SetPercentage(double percentage)
    {
        lock (_sync)
        {
            _percentage = Math.Clamp(percentage, 0.0, 100.0);
        }
    }

    public void SetAcConnected(bool connected)
    {
        lock (_sync)
        {
            _isAcConnected = connected;
        }
    }

    public void SetDischargeRateWatts(double watts)
    {
        lock (_sync)
        {
            _rateWatts = Math.Abs(watts);
            _isAcConnected = false;
        }
    }

    public void SetChargeRateWatts(double watts)
    {
        lock (_sync)
        {
            _rateWatts = Math.Abs(watts);
            _isAcConnected = true;
        }
    }

    public void SetCycleCount(int cycles)
    {
        lock (_sync)
        {
            _cycleCount = Math.Max(0, cycles);
        }
    }

    public void ResetToDefaultVictusProfile()
    {
        lock (_sync)
        {
            _percentage = 78.5;
            _isAcConnected = false;
            _designCapacityMwh = 70070.0;
            _fullChargeCapacityMwh = 66500.0;
            _rateWatts = 18.5;
            _cycleCount = 42;
        }
    }

    public Task<BatteryReading> GetReadingAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var powerStatus = _isAcConnected ? AcPowerStatus.Online : AcPowerStatus.Offline;

            BatteryChargingState chargingState;
            double chargeRateMilliwatts;

            if (_isAcConnected)
            {
                if (_percentage >= 99.5)
                {
                    chargingState = BatteryChargingState.Full;
                    chargeRateMilliwatts = 0.0;
                }
                else
                {
                    chargingState = BatteryChargingState.Charging;
                    chargeRateMilliwatts = _rateWatts * 1000.0; // Positive for charging
                }
            }
            else
            {
                if (_percentage <= 10.0)
                {
                    chargingState = BatteryChargingState.Critical;
                }
                else
                {
                    chargingState = BatteryChargingState.Discharging;
                }
                chargeRateMilliwatts = -(_rateWatts * 1000.0); // Negative for discharging
            }

            var remainingMwh = (_percentage / 100.0) * _fullChargeCapacityMwh;

            var reading = new BatteryReading
            {
                Timestamp = DateTimeOffset.UtcNow,
                AvailableSensors = SensorCapabilities.All,
                BatteryPercentage = TelemetryValue<double>.Simulated(Math.Round(_percentage, 1)),
                PowerStatus = TelemetryValue<AcPowerStatus>.Simulated(powerStatus),
                ChargingState = TelemetryValue<BatteryChargingState>.Simulated(chargingState),
                RemainingCapacityMwh = TelemetryValue<double>.Simulated(Math.Round(remainingMwh, 0)),
                FullChargeCapacityMwh = TelemetryValue<double>.Simulated(_fullChargeCapacityMwh),
                DesignCapacityMwh = TelemetryValue<double>.Simulated(_designCapacityMwh),
                ChargeRateMilliwatts = TelemetryValue<double>.Simulated(chargeRateMilliwatts),
                CycleCount = TelemetryValue<int>.Simulated(_cycleCount)
            };

            return Task.FromResult(reading);
        }
    }
}
