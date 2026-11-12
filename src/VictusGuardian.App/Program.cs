using System;
using System.Threading.Tasks;
using Avalonia;
using VictusGuardian.App.Diagnostics;

namespace VictusGuardian.App;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0].Equals("--diagnostics", StringComparison.OrdinalIgnoreCase) ||
                               args[0].Equals("-d", StringComparison.OrdinalIgnoreCase)))
        {
            return DiagnosticRunner.RunAsync(args).GetAwaiter().GetResult();
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
