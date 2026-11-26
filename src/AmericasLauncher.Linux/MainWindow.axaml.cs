using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AmericasLauncher.Linux.Models;
using AmericasLauncher.Linux.Services;

namespace AmericasLauncher.Linux;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly LinuxRunnerService _runners = new();
    private readonly LinuxLaunchService _launcher = new();
    private readonly AppConfig _config;
    private readonly List<RunnerProfile> _profiles;
    private Process? _runningProcess;

    public MainWindow()
    {
        InitializeComponent();
        _config = _configService.Load();
        _profiles = ConfigService.LoadProfiles();

        RunnerBox.ItemsSource = _profiles;
        RunnerBox.SelectedItem = _profiles.FirstOrDefault(p => p.Id == _config.SelectedRunnerId) ?? _profiles.FirstOrDefault();
        GameFolderBox.Text = _config.GameFolder;
        GameExeBox.Text = _config.GameExe;

        UpdateRunnerUi();
        Loaded += async (_, _) => await CheckAllRunnersHealthAsync();
    }

    private RunnerProfile? Selected => RunnerBox.SelectedItem as RunnerProfile;

    private void Log(string msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogBox.Text += msg + Environment.NewLine;
            LogBox.CaretIndex = LogBox.Text.Length;
        });
    }

    private void NavLaunch_Click(object? sender, RoutedEventArgs e)
    {
        LaunchView.IsVisible = true;
        SettingsView.IsVisible = false;
        NavLaunchBtn.Background = Brush.Parse("#24304A");
        NavSettingsBtn.Background = Brushes.Transparent;
    }

    private void NavSettings_Click(object? sender, RoutedEventArgs e)
    {
        LaunchView.IsVisible = false;
        SettingsView.IsVisible = true;
        NavSettingsBtn.Background = Brush.Parse("#24304A");
        NavLaunchBtn.Background = Brushes.Transparent;
        var cacheBytes = LinuxRunnerService.GetDirectorySize(LinuxRunnerService.PipelineCacheDir);
        CacheSizeText.Text = $"Pipeline Cache ({LinuxRunnerService.PipelineCacheDir}): {LinuxRunnerService.FormatBytes(cacheBytes)}";
    }

    private void RunnerBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Selected is { } p)
        {
            _config.SelectedRunnerId = p.Id;
            _configService.Save(_config);
        }
        UpdateRunnerUi();
    }

    private void UpdateRunnerUi()
    {
        var p = Selected;
        if (p == null)
        {
            RunnerStatusLegendText.Text = "Please select a runner";
            InstallBtn.IsEnabled = false;
            return;
        }

        RunnerStatusLegendText.Text = p.HealthState switch
        {
            RunnerHealthState.UpToDate => "Green: Runner is installed and up to date.",
            RunnerHealthState.Broken => "Red: Runner repository was deleted or is no longer working.",
            RunnerHealthState.UpdateAvailable => "Yellow: Runner is installed, but an update is available.",
            _ => "Black: Runner is not installed yet."
        };
        InstallBtn.IsEnabled = p.HealthState != RunnerHealthState.Broken;

        var pipe = _config.RunnerPipeline.TryGetValue(p.Id, out var pv) ? pv : _config.UsePipeline;
        PipelineToggleBtn.IsChecked = pipe;
        PipelineToggleBtn.Content = pipe ? "Pipeline: ON" : "Pipeline: OFF";

        var seriesS = _config.SeriesS.TryGetValue(p.Id, out var sv) && sv;
        ConsoleBtn.Content = seriesS ? "Xbox Series S" : "Xbox Series X";
    }

    private async Task CheckAllRunnersHealthAsync()
    {
        await Task.WhenAll(_profiles.Select(async p =>
        {
            var state = await _runners.CheckHealthAsync(p);
            Dispatcher.UIThread.Post(() =>
            {
                p.HealthState = state;
                if (Selected?.Id == p.Id) UpdateRunnerUi();
            });
        }));
    }

    private void PipelineToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        var on = PipelineToggleBtn.IsChecked == true;
        _config.RunnerPipeline[p.Id] = on;
        _configService.Save(_config);
        PipelineToggleBtn.Content = on ? "Pipeline: ON" : "Pipeline: OFF";
    }

    private void Console_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        var next = !(_config.SeriesS.TryGetValue(p.Id, out var cur) && cur);
        _config.SeriesS[p.Id] = next;
        _configService.Save(_config);
        ConsoleBtn.Content = next ? "Xbox Series S" : "Xbox Series X";
    }

    private void ResolutionBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (ResolutionBox.SelectedItem is ComboBoxItem item)
        {
            _config.RunnerResolution[p.Id] = item.Content?.ToString() ?? "1080p";
            _configService.Save(_config);
        }
    }

    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        InstallBtn.IsEnabled = false;
        StatusText.Text = "Installing / Updating...";
        try
        {
            var ok = await _runners.InstallOrUpdateAsync(p, _config, Log);
            _configService.Save(_config);
            StatusText.Text = ok ? "Ready" : "Failed";
            p.HealthState = await _runners.CheckHealthAsync(p);
            UpdateRunnerUi();
        }
        finally
        {
            InstallBtn.IsEnabled = p.HealthState != RunnerHealthState.Broken;
        }
    }

    private async void Launch_Click(object? sender, RoutedEventArgs e)
    {
        if (_runningProcess != null && !_runningProcess.HasExited)
        {
            try { _runningProcess.Kill(entireProcessTree: true); Log("Game stopped."); } catch { }
            return;
        }

        if (Selected is not { } p) return;
        if (string.IsNullOrWhiteSpace(_config.GameFolder) || string.IsNullOrWhiteSpace(_config.GameExe))
        {
            Log("Please select Game Folder and Game Exe in Settings.");
            return;
        }

        try
        {
            var pipe = _config.RunnerPipeline.TryGetValue(p.Id, out var pv) ? pv : _config.UsePipeline;
            var seriesS = _config.SeriesS.TryGetValue(p.Id, out var sv) && sv;
            var res = _config.RunnerResolution.TryGetValue(p.Id, out var rv) ? rv : "1080p";

            _runningProcess = await _launcher.Launch(
                p, _config.GameFolder, _config.GameExe, Log, pipe, seriesS, res,
                onExited: () => Dispatcher.UIThread.Post(() =>
                {
                    _runningProcess = null;
                    LaunchBtn.Content = "▶  Launch Game";
                    StatusText.Text = "Ready";
                }));

            LaunchBtn.Content = "■  Stop Game";
            StatusText.Text = "Running";
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
        }
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Game Folder" });
        if (folders.Count > 0)
        {
            GameFolderBox.Text = _config.GameFolder = folders[0].Path.LocalPath;
            _configService.Save(_config);
        }
    }

    private async void BrowseExe_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select Game Executable" });
        if (files.Count > 0)
        {
            GameExeBox.Text = _config.GameExe = files[0].Path.LocalPath;
            _configService.Save(_config);
        }
    }

    private void CoolStart_Click(object? sender, RoutedEventArgs e)
    {
        if (Directory.Exists(LinuxPaths.Root))
            Directory.Delete(LinuxPaths.Root, recursive: true);
        if (Directory.Exists(LinuxRunnerService.PipelineCacheDir))
            Directory.Delete(LinuxRunnerService.PipelineCacheDir, recursive: true);

        _config.InstalledVersions.Clear();
        _configService.Save(_config);
        CacheSizeText.Text = "Pipeline Cache: 0 B";
        _ = CheckAllRunnersHealthAsync();
        Log("Cool Start complete. Local files reset.");
    }
}
