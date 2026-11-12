using System;

namespace VictusGuardian.Windows.Startup;

public interface IWindowsStartupService
{
    bool IsSupported { get; }
    bool IsRegistered();
    bool Register(bool startMinimized = false, string? customExecutablePath = null);
    bool Unregister();
}
