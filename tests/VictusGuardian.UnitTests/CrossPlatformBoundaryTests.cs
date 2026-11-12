using System;
using System.Linq;
using System.Reflection;
using VictusGuardian.Core.Models;
using Xunit;

namespace VictusGuardian.UnitTests;

public class CrossPlatformBoundaryTests
{
    [Fact]
    public void CoreAssembly_HasNoPlatformSpecificDependencies()
    {
        var coreAssembly = typeof(BatteryReading).Assembly;
        var referencedAssemblies = coreAssembly.GetReferencedAssemblies();

        var forbiddenTokens = new[]
        {
            "Windows",
            "Win32",
            "System.Management",
            "Avalonia",
            "Microsoft.Data.Sqlite"
        };

        foreach (var refAsm in referencedAssemblies)
        {
            foreach (var forbidden in forbiddenTokens)
            {
                Assert.DoesNotContain(forbidden, refAsm.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
