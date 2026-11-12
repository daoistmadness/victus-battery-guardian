using System;
using System.Reflection;

namespace VictusGuardian.App.Diagnostics;

public static class AppVersion
{
    public static string DisplayVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.3.0";

    public static string FullVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.3.0.0";

    public static string CommitSha =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev-local";
}
