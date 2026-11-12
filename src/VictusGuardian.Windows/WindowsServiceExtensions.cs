using System;
using Microsoft.Extensions.DependencyInjection;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Windows.Composite;
using VictusGuardian.Windows.Native;
using VictusGuardian.Windows.Power;
using VictusGuardian.Windows.Telemetry;

namespace VictusGuardian.Windows;

public static class WindowsServiceExtensions
{
    public static IServiceCollection AddWindowsGuardianServices(this IServiceCollection services)
    {
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<INativeBatteryDeviceApi, WindowsNativeBatteryDeviceApi>();
        }
        else
        {
            services.AddSingleton<INativeBatteryDeviceApi, UnsupportedNativeBatteryDeviceApi>();
        }

        services.AddSingleton<IWindowsIoctlBatteryReader, WindowsIoctlBatteryReader>();
        services.AddSingleton<IWindowsPowerStatusReader, WindowsPowerStatusReader>();
        services.AddSingleton<INativePowerEvents, WindowsPowerEventNotifier>();
        services.AddSingleton<WindowsBatteryTelemetryProvider>();
        services.AddSingleton<CompositeBatteryTelemetryProvider>();
        services.AddSingleton<ICompositeBatteryTelemetryProvider>(sp => sp.GetRequiredService<CompositeBatteryTelemetryProvider>());

        return services;
    }
}
