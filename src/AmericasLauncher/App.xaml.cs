// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.Windows;

namespace AmericasLauncher;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            ReportCrash(e.Exception);
            e.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);
    }

    private static void ReportCrash(Exception? ex)
    {
        var text = ex?.ToString() ?? "Unknown error";
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AmericasLauncher");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "crash.log"), DateTime.Now + Environment.NewLine + text);
        }
        catch { /* logging must never crash the app */ }

        MessageBox.Show(text, "Americas Launcher crashed", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
