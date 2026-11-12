using System;

namespace VictusGuardian.Windows.Power;

public enum PowerStateChangeType
{
    Suspend = 1,
    Resume = 2,
    StatusChanged = 3
}

public interface INativePowerEvents : IDisposable
{
    event Action<PowerStateChangeType>? PowerStateChanged;
    void NotifyStateChange(PowerStateChangeType changeType);
}
