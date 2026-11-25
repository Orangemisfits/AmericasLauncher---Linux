// SPDX-License-Identifier: GPL-2.0-only
using System.Runtime.Versioning;
using Avalonia;
using AmericasLauncher.Services;

[assembly: SupportedOSPlatform("linux")]

namespace AmericasLauncher;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.SetObserved();
        };

        LinuxEnvironment.PreparePath();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Matches StartupWMClass in the .desktop file, so docks show the launcher's icon.
            .With(new X11PlatformOptions { WmClass = "AmericasLauncher" })
            .WithInterFont()
            .LogToTrace();
}
