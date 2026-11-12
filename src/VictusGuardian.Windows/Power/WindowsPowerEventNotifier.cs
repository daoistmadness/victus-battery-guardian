using System;
using System.Runtime.Versioning;

namespace VictusGuardian.Windows.Power;

public class WindowsPowerEventNotifier : INativePowerEvents
{
    public event Action<PowerStateChangeType>? PowerStateChanged;
    private bool _isDisposed;

    public WindowsPowerEventNotifier()
    {
        if (OperatingSystem.IsWindows())
        {
            HookSystemEvents();
        }
    }

    [SupportedOSPlatform("windows")]
    private void HookSystemEvents()
    {
        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch
        {
            // Graceful fallback if running in non-interactive/headless Windows session
        }
    }

    [SupportedOSPlatform("windows")]
    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        var mapped = e.Mode switch
        {
            Microsoft.Win32.PowerModes.Suspend => PowerStateChangeType.Suspend,
            Microsoft.Win32.PowerModes.Resume => PowerStateChangeType.Resume,
            Microsoft.Win32.PowerModes.StatusChange => PowerStateChangeType.StatusChanged,
            _ => PowerStateChangeType.StatusChanged
        };

        PowerStateChanged?.Invoke(mapped);
    }

    public void NotifyStateChange(PowerStateChangeType changeType)
    {
        PowerStateChanged?.Invoke(changeType);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch
            {
            }
        }
    }
}
