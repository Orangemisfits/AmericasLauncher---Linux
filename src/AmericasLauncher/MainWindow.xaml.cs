// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;
using AmericasLauncher.Models;
using AmericasLauncher.Services;
using Microsoft.Win32;

namespace AmericasLauncher;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly RunnerService _runners = new();
    private readonly LaunchService _launcher = new();
    private readonly LauncherUpdateService _launcherUpdates = new();
    private readonly PortStatusService _portStatus = new();
    private readonly AppConfig _config;
    private readonly List<RunnerProfile> _profiles;
    private bool _portLoadedOnce;
    private string? _latestReleaseUrl;

    public MainWindow()
    {
        InitializeComponent();
        _config = _configService.Load();
        _profiles = ConfigService.LoadProfiles();

        RunnerBox.ItemsSource = _profiles;
        RunnerBox.SelectedItem = _profiles.FirstOrDefault(p => p.Id == _config.SelectedRunnerId) ?? _profiles.FirstOrDefault();
        GameFolderBox.Text = _config.GameFolder;
        GameExeBox.Text = _config.GameExe;
        PipelineToggleButton.IsChecked = _config.UsePipeline;
        UpdateMissingPathsBanner();
        UpdatePipelineState();

        // Attached after the initial selection so it does not fire during setup.
        RunnerBox.SelectionChanged += async (_, _) =>
        {
            if (Selected is { } profile)
            {
                _config.SelectedRunnerId = profile.Id;
                _configService.Save(_config);
            }
            UpdatePipelineState();
            await RefreshVersionAsync();
        };
        Loaded += async (_, _) => await RefreshVersionAsync();
        StateChanged += (_, _) =>
        {
            var isMax = WindowState == WindowState.Maximized;
            RootGrid.Margin = isMax ? new Thickness(7) : new Thickness(0);
            MaxRestoreIcon.Data = Geometry.Parse(isMax ? "M0,2 H7 V9 H0 Z M2,2 V0 H9 V7 H7" : "M0,0 H9 V9 H0 Z");
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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

    private void UpdateMissingPathsBanner()
    {
        if (MissingPathsBanner == null) return;
        var missing = string.IsNullOrWhiteSpace(_config.GameFolder) || string.IsNullOrWhiteSpace(_config.GameExe);
        MissingPathsBanner.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePipelineState()
    {
        if (PipelineToggleButton == null || PipelineWarningBanner == null) return;
        var supportsPipeline = Selected is { PreLaunchArgs.Count: > 0 };
        PipelineToggleButton.IsEnabled = supportsPipeline;
        var showWarning = supportsPipeline && PipelineToggleButton.IsChecked == true;
        PipelineWarningBanner.Visibility = showWarning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PipelineToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (PipelineToggleButton == null) return;
        _config.UsePipeline = PipelineToggleButton.IsChecked == true;
        _configService.Save(_config);
        UpdatePipelineState();
    }

    private async void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (LaunchView == null || PortStatusView == null || SettingsView == null) return;
        LaunchView.Visibility = LaunchNavButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PortStatusView.Visibility = PortStatusNavButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = SettingsNavButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (PortStatusNavButton.IsChecked == true && !_portLoadedOnce)
        {
            _portLoadedOnce = true;
            await LoadPortStatusAsync();
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private async void PortRefresh_Click(object sender, RoutedEventArgs e) => await LoadPortStatusAsync();

    private async Task LoadPortStatusAsync()
    {
        PortRefreshButton.IsEnabled = false;
        PortUpdatedText.Text = "Loading...";
        try
        {
            var report = await _portStatus.FetchAsync();
            PortUpdatedText.Text = string.IsNullOrWhiteSpace(report.UpdatedAt) ? "Updated" : $"Updated: {report.UpdatedAt}";
            PortLeaderNameText.Text = report.LeaderName;
            PortLeaderRepoText.Text = report.LeaderRepo;
            PortOverallScoreText.Text = $"{report.LeaderScore:0.0}%";
            PortTop3Text.Text = $"Top 3 avg: {report.Top3Average:0.0}% · Reached: {report.ReachedLabel}";
            PortOverallProgressBar.Value = report.LeaderScore;
            PortMilestonesList.ItemsSource = report.Milestones;
            PortStoryText.Text = report.StoryLabel;
            PortRenderText.Text = report.RenderLabel;
            PortPerfText.Text = $"{report.Fps:0.#} FPS";
            PortStabilityText.Text = report.StabilityLabel;
            PortChangesList.ItemsSource = report.Changes;
        }
        catch (Exception ex)
        {
            PortUpdatedText.Text = "Failed to load: " + ex.Message;
        }
        finally
        {
            PortRefreshButton.IsEnabled = true;
        }
    }

    private async void CheckLauncherUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckLauncherUpdateButton.IsEnabled = false;
        OpenReleaseButton.Visibility = Visibility.Collapsed;
        LauncherUpdateStatusText.Text = "Checking for updates...";
        LauncherUpdateStatusText.Foreground = Brushes.LightSlateGray;

        try
        {
            var res = await _launcherUpdates.CheckForUpdateAsync();
            LauncherUpdateStatusText.Text = res.Message;
            LauncherUpdateStatusText.Foreground = res.IsUpdateAvailable
                ? Brushes.Orange
                : res.HasRelease ? Brushes.LimeGreen : Brushes.LightSlateGray;

            _latestReleaseUrl = res.ReleaseUrl;
            OpenReleaseButton.Visibility = res.IsUpdateAvailable && !string.IsNullOrWhiteSpace(res.ReleaseUrl)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            LauncherUpdateStatusText.Text = "Could not check for updates: " + ex.Message;
            LauncherUpdateStatusText.Foreground = Brushes.IndianRed;
        }
        finally
        {
            CheckLauncherUpdateButton.IsEnabled = true;
        }
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_latestReleaseUrl))
            Process.Start(new ProcessStartInfo(_latestReleaseUrl) { UseShellExecute = true });
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
                SetStatus("Not installed", Brushes.LightSlateGray);
                return;
            }

            var version = await _runners.GetInstalledVersionAsync(profile);
            if (ticket != _refreshCounter) return;
            VersionText.Text = "Emulator version: " + (version == "" ? "unknown" : version);
            SetStatus("Checking for updates...", Brushes.LightSlateGray);

            var status = await _runners.CheckForUpdateAsync(profile);
            if (ticket != _refreshCounter) return;

            switch (status)
            {
                case UpdateStatus.UpToDate:
                    SetStatus("Up to date", Brushes.LimeGreen);
                    break;
                case UpdateStatus.UpdateAvailable:
                    SetStatus("New update available", Brushes.Orange);
                    break;
                default:
                    SetStatus("Could not check for updates", Brushes.LightSlateGray);
                    break;
            }
        }
        catch
        {
            if (ticket == _refreshCounter) SetStatus("Could not check for updates", Brushes.LightSlateGray);
        }
    }

    private void SetBusy(bool busy)
    {
        InstallButton.IsEnabled = !busy;
        LaunchButton.IsEnabled = !busy;
        RunnerBox.IsEnabled = !busy;
        Progress.IsIndeterminate = busy;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select the game folder" };
        if (dlg.ShowDialog() == true)
        {
            GameFolderBox.Text = _config.GameFolder = dlg.FolderName;
            _configService.Save(_config);
            UpdateMissingPathsBanner();
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
            UpdateMissingPathsBanner();
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
        if (string.IsNullOrWhiteSpace(_config.GameFolder) || string.IsNullOrWhiteSpace(_config.GameExe))
        {
            UpdateMissingPathsBanner();
            Log("Game Folder and Game Exe are not selected. Please select them in Settings.");
            return;
        }

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
            var usePipeline = PipelineToggleButton.IsEnabled && PipelineToggleButton.IsChecked == true;
            await _launcher.Launch(profile, _config.GameFolder, _config.GameExe, Log, usePipeline);
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
