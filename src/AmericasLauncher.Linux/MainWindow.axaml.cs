// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AmericasLauncher.Models;
using AmericasLauncher.Services;

namespace AmericasLauncher;

public partial class MainWindow : Window
{
    private const int MaxLogChars = 120_000;

    private readonly ConfigService _configService = new();
    private readonly RunnerService _runners = new();
    private readonly LaunchService _launcher = new();
    private readonly WineRuntimeService _runtime = new();
    private readonly LauncherUpdateService _launcherUpdates = new();
    private readonly PortStatusService _portStatus = new();
    private readonly AppConfig _config;
    private readonly List<RunnerProfile> _profiles;
    private bool _portLoadedOnce;
    private string? _latestReleaseUrl;
    private int _refreshCounter;

    // Work that Stop can end: setup/build/pre-launch (cancellation) and the game process.
    private CancellationTokenSource? _operationCts;
    private Process? _gameProcess;
    private bool _launching;
    private bool _closeRequestedWhileRunning;

    // Output arrives from background threads, often thousands of lines during a build: it is queued
    // and written to the log box ten times a second, and to launcher.log.
    private readonly ConcurrentQueue<string> _pendingLog = new();
    private readonly StringBuilder _logText = new();
    private readonly DispatcherTimer _logTimer;
    private StreamWriter? _logFile;

    public MainWindow()
    {
        InitializeComponent();

        _config = _configService.Load();
        _profiles = ConfigService.LoadProfiles();

        var version = LauncherUpdateService.CurrentVersionString;
        TitleText.Text = $"Americas Launcher - V{version} · Linux";
        SidebarVersionText.Text = $"v{version} · Linux";
        LauncherUpdateStatusText.Text = $"Current version: v{version}";

        _logTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => FlushLog());
        _logTimer.Start();
        OpenLogFile();

        RunnerBox.ItemsSource = _profiles;
        RunnerBox.SelectedItem = _profiles.FirstOrDefault(p => p.Id == _config.SelectedRunnerId) ?? _profiles.FirstOrDefault();
        GameFolderBox.Text = _config.GameFolder;
        GameExeBox.Text = _config.GameExe;
        UpdateMissingPathsBanner();
        LoadRuntimeSettings();

        // The paths can also be typed or pasted (useful without a desktop file chooser).
        GameFolderBox.LostFocus += (_, _) => SaveGamePathText();
        GameExeBox.LostFocus += (_, _) => SaveGamePathText();

        // Attached after the initial selection so it does not fire during setup.
        RunnerBox.SelectionChanged += async (_, _) =>
        {
            if (Selected is { } profile)
            {
                _config.SelectedRunnerId = profile.Id;
                _configService.Save(_config);
            }
            await RefreshVersionAsync();
        };
        Opened += async (_, _) => await RefreshVersionAsync();
    }

    private RunnerProfile? Selected => RunnerBox.SelectedItem as RunnerProfile;

    // ---------------------------------------------------------------- window chrome

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && MaxRestoreIcon != null)
        {
            var isMax = WindowState == WindowState.Maximized;
            MaxRestoreIcon.Data = Geometry.Parse(isMax ? "M0,2 H7 V9 H0 Z M2,2 V0 H9 V7 H7" : "M0,0 H9 V9 H0 Z");
            GripsPanel.IsVisible = WindowState == WindowState.Normal;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) ToggleMaximize();
        else BeginMoveDrag(e);
    }

    private void Grip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string edge } && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            Enum.TryParse<WindowEdge>(edge, out var windowEdge))
        {
            BeginResizeDrag(windowEdge, e);
            e.Handled = true;
        }
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        var running = _gameProcess is { HasExited: false } || _operationCts != null;
        if (running && !_closeRequestedWhileRunning)
        {
            // The runner writes into the launcher's log: closing would cut its output off.
            e.Cancel = true;
            _closeRequestedWhileRunning = true;
            LaunchNavButton.IsChecked = true;
            Log("The game or a setup is still running. Click Stop, or close the launcher again to stop it and exit.");
            return;
        }

        if (running)
        {
            e.Cancel = true;
            await StopAllAsync();
            _closeRequestedWhileRunning = false;
            _gameProcess = null;
            _operationCts = null;
            Close();
            return;
        }

        _logTimer.Stop();
        FlushLog();
        _logFile?.Dispose();
    }

    // ---------------------------------------------------------------- log

    private void Log(string msg) => _pendingLog.Enqueue(AnsiEscape().Replace(msg, ""));

    private void FlushLog()
    {
        if (_pendingLog.IsEmpty) return;

        var chunk = new StringBuilder();
        while (_pendingLog.TryDequeue(out var line)) chunk.Append(line).Append('\n');

        try
        {
            _logFile?.Write(chunk);
            _logFile?.Flush();
        }
        catch { /* the on-screen log still works */ }

        _logText.Append(chunk);
        if (_logText.Length > MaxLogChars)
        {
            var cut = _logText.Length - MaxLogChars;
            var text = _logText.ToString();
            var nl = text.IndexOf('\n', cut);
            _logText.Remove(0, nl >= 0 ? nl + 1 : cut);
        }

        LogBox.Text = _logText.ToString();
        LogBox.CaretIndex = LogBox.Text.Length;
    }

    private void OpenLogFile()
    {
        try
        {
            Directory.CreateDirectory(Paths.Root);
            var path = Path.Combine(Paths.Root, "launcher.log");
            if (File.Exists(path) && new FileInfo(path).Length > 20 * 1024 * 1024)
                File.Move(path, path + ".old", overwrite: true);
            _logFile = new StreamWriter(path, append: true, new UTF8Encoding(false));
            _logFile.WriteLine($"----- {DateTime.Now:yyyy-MM-dd HH:mm:ss} Americas Launcher {LauncherUpdateService.CurrentVersionString} (Linux) -----");
        }
        catch { _logFile = null; }
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiEscape();

    // ---------------------------------------------------------------- navigation

    private async void Nav_Checked(object? sender, RoutedEventArgs e)
    {
        if (LaunchView == null || PortStatusView == null || SettingsView == null) return;
        LaunchView.IsVisible = LaunchNavButton.IsChecked == true;
        PortStatusView.IsVisible = PortStatusNavButton.IsChecked == true;
        SettingsView.IsVisible = SettingsNavButton.IsChecked == true;

        if (PortStatusNavButton.IsChecked == true && !_portLoadedOnce)
        {
            _portLoadedOnce = true;
            await LoadPortStatusAsync();
        }
        if (SettingsNavButton.IsChecked == true) UpdateRuntimeStatus();
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }   // xdg-open
        catch { /* no desktop handler */ }
    }

    // ---------------------------------------------------------------- port status

    private async void PortRefresh_Click(object? sender, RoutedEventArgs e) => await LoadPortStatusAsync();

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

    // ---------------------------------------------------------------- launcher updates

    private async void CheckLauncherUpdate_Click(object? sender, RoutedEventArgs e)
    {
        CheckLauncherUpdateButton.IsEnabled = false;
        OpenReleaseButton.IsVisible = false;
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
            OpenReleaseButton.IsVisible = res.IsUpdateAvailable && !string.IsNullOrWhiteSpace(res.ReleaseUrl);
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

    private void OpenRelease_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_latestReleaseUrl)) OpenUrl(_latestReleaseUrl);
    }

    // ---------------------------------------------------------------- runner version

    private void SetStatus(string text, IBrush color)
    {
        StatusText.Text = text;
        StatusText.Foreground = color;
    }

    private void UpdateMissingPathsBanner()
    {
        var missing = string.IsNullOrWhiteSpace(_config.GameFolder) || string.IsNullOrWhiteSpace(_config.GameExe);
        MissingPathsBanner.IsVisible = missing;
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
            if (profile.Linux == null)
            {
                VersionText.Text = "Emulator version: -";
                SetStatus("Not available on Linux yet", Brushes.LightSlateGray);
                return;
            }

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
        PrepareRuntimeButton.IsEnabled = !busy;
        RuntimeBox.IsEnabled = !busy;
        Progress.IsIndeterminate = busy;
        UpdateStopButton();
    }

    private void UpdateStopButton() =>
        StopButton.IsEnabled = _operationCts != null || _gameProcess is { HasExited: false };

    // ---------------------------------------------------------------- game paths

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the game folder",
            AllowMultiple = false,
            SuggestedStartLocation = await StartLocationAsync(_config.GameFolder)
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;

        GameFolderBox.Text = _config.GameFolder = path;
        _configService.Save(_config);
        UpdateMissingPathsBanner();
    }

    private async void BrowseExe_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the game exe (Game.exe)",
            AllowMultiple = false,
            SuggestedStartLocation = await StartLocationAsync(_config.GameFolder),
            FileTypeFilter =
            [
                new FilePickerFileType("Game executable (*.exe)") { Patterns = ["*.exe", "*.EXE"] },
                FilePickerFileTypes.All
            ]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;

        GameExeBox.Text = _config.GameExe = path;

        // Convenience: if no folder was chosen yet, use the exe's folder.
        if (string.IsNullOrWhiteSpace(_config.GameFolder))
            GameFolderBox.Text = _config.GameFolder = Path.GetDirectoryName(path) ?? "";

        _configService.Save(_config);
        UpdateMissingPathsBanner();
    }

    private void SaveGamePathText()
    {
        static string Clean(string? text) =>
            string.IsNullOrWhiteSpace(text) ? "" : LinuxEnvironment.ExpandHome(text.Trim().Trim('"', '\''));

        var folder = Clean(GameFolderBox.Text);
        var exe = Clean(GameExeBox.Text);
        if (folder == _config.GameFolder && exe == _config.GameExe) return;

        GameFolderBox.Text = _config.GameFolder = folder;
        GameExeBox.Text = _config.GameExe = exe;
        _configService.Save(_config);
        UpdateMissingPathsBanner();
    }

    private async Task<IStorageFolder?> StartLocationAsync(string? dir)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)
                ? await StorageProvider.TryGetFolderFromPathAsync(dir)
                : null;
        }
        catch { return null; }
    }

    // ---------------------------------------------------------------- Windows runtime settings

    private void LoadRuntimeSettings()
    {
        RuntimeBox.SelectedIndex = _config.Runtime == WineRuntimeKind.Proton ? 1 : 0;
        WineCommandBox.Text = _config.WineCommand;
        ProtonPathBox.Text = _config.ProtonPath;
        PrefixBox.Text = _config.WinePrefix;
        UpdateRuntimeUi();

        RuntimeBox.SelectionChanged += (_, _) =>
        {
            _config.Runtime = RuntimeBox.SelectedIndex == 1 ? WineRuntimeKind.Proton : WineRuntimeKind.Wine;
            _configService.Save(_config);
            UpdateRuntimeUi();
        };
        WineCommandBox.LostFocus += (_, _) => SaveRuntimeText();
        ProtonPathBox.LostFocus += (_, _) => SaveRuntimeText();
        PrefixBox.LostFocus += (_, _) => SaveRuntimeText();
    }

    private void SaveRuntimeText()
    {
        _config.WineCommand = string.IsNullOrWhiteSpace(WineCommandBox.Text) ? "wine" : WineCommandBox.Text.Trim();
        _config.ProtonPath = ProtonPathBox.Text?.Trim() ?? "";
        _config.WinePrefix = PrefixBox.Text?.Trim() ?? "";
        _configService.Save(_config);
        UpdateRuntimeUi();
    }

    private void UpdateRuntimeUi()
    {
        var proton = _config.Runtime == WineRuntimeKind.Proton;
        WinePanel.IsVisible = !proton;
        ProtonPanel.IsVisible = proton;
        var prefix = proton ? Paths.DefaultPrefix + "-proton" : Paths.DefaultPrefix;
        if (prefix.StartsWith(LinuxEnvironment.Home + "/", StringComparison.Ordinal))
            prefix = "~" + prefix[LinuxEnvironment.Home.Length..];
        PrefixBox.Watermark = prefix + "  (default)";
        UpdateRuntimeStatus();
    }

    private void UpdateRuntimeStatus()
    {
        var (path, problem) = WineRuntimeService.ResolveRuntime(_config);
        if (path != null)
        {
            RuntimeFoundText.Text = "Found: " + path;
            RuntimeFoundText.Foreground = Brushes.LimeGreen;
        }
        else
        {
            RuntimeFoundText.Text = problem;
            RuntimeFoundText.Foreground = Brushes.Orange;
        }
    }

    private async void BrowseWine_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a wine binary (e.g. .../bin/wine)",
            AllowMultiple = false
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        WineCommandBox.Text = path;
        SaveRuntimeText();
    }

    private async void BrowsePrefix_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the Wine prefix folder",
            AllowMultiple = false
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        PrefixBox.Text = path;
        SaveRuntimeText();
    }

    private async void PrepareRuntime_Click(object? sender, RoutedEventArgs e)
    {
        SaveRuntimeText();
        RuntimeStatusText.Text = "Preparing... (progress is shown in the log on the Launch page)";
        var ok = await RunOperationAsync(ct => _runtime.PrepareAsync(_config, Log, ct));
        RuntimeStatusText.Text = ok ? "Runtime ready." : "The runtime is not ready: see the log on the Launch page.";
    }

    private void CheckRequirements_Click(object? sender, RoutedEventArgs e)
    {
        SaveRuntimeText();
        LaunchNavButton.IsChecked = true;
        Log("=== Linux requirements ===");

        var ids = _profiles.Where(p => p.Linux != null).SelectMany(p => p.Linux!.Dependencies).Distinct().ToList();
        var missingSystem = new List<string>();
        foreach (var id in ids)
        {
            var found = DependencyService.IsInstalled(id);
            var note = !found && !DependencyService.IsSystemPackage(id) ? " (installed automatically by Install / Update)" : "";
            Log($"[{(found ? "ok" : "!!")}] {DependencyService.DisplayName(id)}{(found ? "" : " is missing")}{note}");
            if (!found && DependencyService.IsSystemPackage(id)) missingSystem.Add(id);
        }

        var (runtime, problem) = WineRuntimeService.ResolveRuntime(_config);
        Log(runtime != null ? $"[ok] Runtime: {runtime}" : "[!!] " + problem);
        Log($"     Wine prefix: {WineRuntimeService.PrefixDir(_config)}" +
            (WineRuntimeService.IsPrefixReady(_config) ? "" : " (not prepared yet)"));
        Log("     A Vulkan driver with mesh shader support is needed (Mesa RADV, NVIDIA proprietary ...).");

        if (missingSystem.Count > 0)
            Log("Install the missing packages with: " + LinuxEnvironment.InstallHint(missingSystem));
        else if (runtime != null)
            Log("Everything needed to build and run the runners was found.");
    }

    private void OpenDataFolder_Click(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Paths.Root);
        OpenUrl(Paths.Root);
    }

    // ---------------------------------------------------------------- install / launch / stop

    /// <summary>Runs a cancellable operation with the UI in its busy state and the log in view.</summary>
    private async Task<bool> RunOperationAsync(Func<CancellationToken, Task<bool>> operation)
    {
        LaunchNavButton.IsChecked = true;
        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        SetBusy(true);
        try
        {
            return await operation(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Stopped.");
            return false;
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            return false;
        }
        finally
        {
            _operationCts = null;
            SetBusy(false);
        }
    }

    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        SaveRuntimeText();

        var ok = await RunOperationAsync(ct => _runners.InstallOrUpdateAsync(profile, _config, Log, ct));
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

    private async void Launch_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        SaveRuntimeText();
        SaveGamePathText();

        if (!_runners.IsInstalled(profile)) { Log("Click 'Install / Update' first."); return; }
        if (string.IsNullOrWhiteSpace(_config.GameFolder) || string.IsNullOrWhiteSpace(_config.GameExe))
        {
            UpdateMissingPathsBanner();
            Log("Game Folder and Game Exe are not selected. Please select them in Settings.");
            return;
        }

        var folder = Path.GetFullPath(_config.GameFolder).TrimEnd('/') + "/";
        var exe = Path.GetFullPath(_config.GameExe);
        if (!exe.StartsWith(folder, StringComparison.Ordinal))
        {
            Log("The game exe must be inside the selected game folder.");
            return;
        }
        if (_gameProcess is { HasExited: false })
        {
            Log("The game is already running. Click Stop first to start it again.");
            return;
        }

        _launching = true;
        var started = await RunOperationAsync(async ct =>
        {
            var process = await _launcher.Launch(profile, _config, Log, ct);
            _gameProcess = process;
            process.Exited += (_, _) => Dispatcher.UIThread.Post(UpdateStopButton);
            return true;
        });
        _launching = false;
        if (started)
        {
            Log("Started.");
            UpdateStopButton();
        }
    }

    private async void Stop_Click(object? sender, RoutedEventArgs e) => await StopAllAsync();

    private async Task StopAllAsync()
    {
        StopButton.IsEnabled = false;
        var stopWine = _launching || _gameProcess is { HasExited: false };
        _operationCts?.Cancel();
        try
        {
            if (stopWine) await LaunchService.StopAsync(_gameProcess, _config, Log);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
        }
        UpdateStopButton();
    }
}
