// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.Windows;
using System.Windows.Media;
using AmericasLauncher.Models;
using AmericasLauncher.Services;
using Microsoft.Win32;

namespace AmericasLauncher;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly RunnerService _runners = new();
    private readonly LaunchService _launcher = new();
    private readonly AppConfig _config;
    private readonly List<RunnerProfile> _profiles;

    public MainWindow()
    {
        InitializeComponent();
        _config = _configService.Load();
        _profiles = ConfigService.LoadProfiles();

        RunnerBox.ItemsSource = _profiles;
        RunnerBox.SelectedItem = _profiles.FirstOrDefault(p => p.Id == _config.SelectedRunnerId) ?? _profiles.FirstOrDefault();
        GameFolderBox.Text = _config.GameFolder;
        GameExeBox.Text = _config.GameExe;

        // Attached after the initial selection so it does not fire during setup.
        RunnerBox.SelectionChanged += async (_, _) => await RefreshVersionAsync();
        Loaded += async (_, _) => await RefreshVersionAsync();
    }

    private RunnerProfile? Selected => RunnerBox.SelectedItem as RunnerProfile;

    // Safe to call from any thread.
    private void Log(string msg) => Dispatcher.Invoke(() =>
    {
        LogBox.AppendText(msg + Environment.NewLine);
        LogBox.ScrollToEnd();
    });

    private int _refreshCounter;

    private void SetStatus(string text, Brush color)
    {
        StatusText.Text = text;
        StatusText.Foreground = color;
    }

    /// <summary>Shows the installed version and whether a newer one exists upstream.</summary>
    private async Task RefreshVersionAsync()
    {
        var ticket = ++_refreshCounter;   // ignore results if the selection changed meanwhile

        if (Selected is not { } profile)
        {
            VersionText.Text = "Emulator version: -";
            SetStatus("", Brushes.Gray);
            return;
        }

        try
        {
            if (!_runners.IsInstalled(profile))
            {
                VersionText.Text = "Emulator version: not installed";
                SetStatus("Not installed", Brushes.Gray);
                return;
            }

            var version = await _runners.GetInstalledVersionAsync(profile);
            if (ticket != _refreshCounter) return;
            VersionText.Text = "Emulator version: " + (version == "" ? "unknown" : version);
            SetStatus("Checking for updates...", Brushes.Gray);

            var status = await _runners.CheckForUpdateAsync(profile);
            if (ticket != _refreshCounter) return;

            switch (status)
            {
                case UpdateStatus.UpToDate:
                    SetStatus("Up to date", Brushes.ForestGreen);
                    break;
                case UpdateStatus.UpdateAvailable:
                    SetStatus("New update available", Brushes.DarkOrange);
                    break;
                default:
                    SetStatus("Could not check for updates", Brushes.Gray);
                    break;
            }
        }
        catch
        {
            if (ticket == _refreshCounter) SetStatus("Could not check for updates", Brushes.Gray);
        }
    }

    private void SetBusy(bool busy)
    {
        InstallButton.IsEnabled = !busy;
        LaunchButton.IsEnabled = !busy;
        Progress.IsIndeterminate = busy;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select the game folder" };
        if (dlg.ShowDialog() == true)
        {
            GameFolderBox.Text = _config.GameFolder = dlg.FolderName;
            _configService.Save(_config);
        }
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select the game exe (Game.exe)",
            Filter = "Game executable (*.exe)|*.exe|All files|*.*"
        };
        if (!string.IsNullOrWhiteSpace(_config.GameFolder) && Directory.Exists(_config.GameFolder))
            dlg.InitialDirectory = _config.GameFolder;

        if (dlg.ShowDialog() == true)
        {
            GameExeBox.Text = _config.GameExe = dlg.FileName;

            // Convenience: if no folder was chosen yet, use the exe's folder.
            if (string.IsNullOrWhiteSpace(_config.GameFolder))
                GameFolderBox.Text = _config.GameFolder = Path.GetDirectoryName(dlg.FileName) ?? "";

            _configService.Save(_config);
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        SetBusy(true);
        try
        {
            var ok = await _runners.InstallOrUpdateAsync(profile, _config, Log);
            _config.SelectedRunnerId = profile.Id;
            _configService.Save(_config);

            if (ok)
            {
                Log("Done. You can launch the game now.");
                if (!string.IsNullOrWhiteSpace(profile.Notes)) Log("NOTE: " + profile.Notes);
            }
            else
            {
                Log("Setup did not finish. Check the messages above.");
            }

            await RefreshVersionAsync();
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

        private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;

        if (!_runners.IsInstalled(profile)) { Log("Click 'Install / Update' first."); return; }
        if (string.IsNullOrWhiteSpace(_config.GameFolder)) { Log("Select the game folder first."); return; }
        if (string.IsNullOrWhiteSpace(_config.GameExe)) { Log("Select the game exe first."); return; }

        var folder = Path.GetFullPath(_config.GameFolder).TrimEnd('\\') + "\\";
        var exe = Path.GetFullPath(_config.GameExe);
        if (!exe.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        {
            Log("The game exe must be inside the selected game folder.");
            return;
        }

        SetBusy(true);
        try
        {
            await _launcher.Launch(profile, _config.GameFolder, _config.GameExe, Log);
            Log("Started.");
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
}
