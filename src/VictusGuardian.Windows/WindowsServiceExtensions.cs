using Microsoft.Extensions.DependencyInjection;
using VictusGuardian.Core.Contracts;
using VictusGuardian.Windows.Telemetry;

namespace VictusGuardian.Windows;

public static class WindowsServiceExtensions
{
    public static IServiceCollection AddWindowsGuardianServices(this IServiceCollection services)
    {
        services.AddSingleton<WindowsBatteryTelemetryProvider>();
        return services;
    }
}
