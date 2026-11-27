// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
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
    private readonly List<RunnerProfile> _launchProfiles;   // _profiles without the Nvidia page runner
    private bool _portLoadedOnce;
    private string? _latestReleaseUrl;
    private string? _latestDownloadUrl;
    private string? _latestVersion;
    private string? _latestReleaseNotes;
    private const int ToastSeconds = 15;

    private DispatcherTimer? _toastTimer;
    private Process? _runningProcess;
    private Task<bool>? _gitAvailability;
    private bool _reportedMissingGit;

    // Non-null from the moment Launch is clicked  until the game has exited or was stopped.
    private CancellationTokenSource? _launchCts;
    private RunnerProfile? _launchProfile;

    public MainWindow()
    {
        InitializeComponent();

        // The version always comes from the assembly (csproj / release tag), never from hard-coded text.
        var version = LauncherUpdateService.CurrentVersionString;
        Title = "Americas Launcher";
        TitleBarText.Text = Title;
        SidebarVersionText.Text = $"v{version}";
        LauncherUpdateStatusText.Text = $"Current version: v{version}";

        _config = _configService.Load();
        _profiles = ConfigService.LoadProfiles();
        var customProfile = _profiles.FirstOrDefault(p => string.Equals(p.Id, "custom", StringComparison.OrdinalIgnoreCase));
        if (customProfile != null && !string.IsNullOrWhiteSpace(_config.CustomRunnerGitUrl))
            customProfile.GitUrl = _config.CustomRunnerGitUrl;
        if (CustomRunnerUrlBox != null)
            CustomRunnerUrlBox.Text = _config.CustomRunnerGitUrl;
        _launchStore = LaunchOptionsStore.Load();   // never throws; an unreadable file is set aside
        if (_launchStore.LoadProblem != null) Log("[!] " + _launchStore.LoadProblem);

        // Nvidia - VIctory Based has its own page (Launch > Nvidia - VIctory), not a card.
        _launchProfiles = _profiles.Where(p => !IsNvidiaProfile(p)).ToList();
        RunnerBox.ItemsSource = _launchProfiles;
        if (RunnersDirectoryList != null)
            RunnersDirectoryList.ItemsSource = _profiles;
        RunnerService.CustomRunnersRoot = _config.CustomRunnersDir;
        RunnerService.CustomCacheRoot = _config.CustomCacheDir;

        RunnerBox.SelectedItem = _launchProfiles.FirstOrDefault(p => p.Id == _config.SelectedRunnerId) ?? _launchProfiles.FirstOrDefault();
        GameFolderBox.Text = _config.GameFolder;
        GameExeBox.Text = _config.GameExe;
        AutoUpdateToggle.IsChecked = _config.AutoUpdate;
        UseRecommendedToggle.IsChecked = _config.UseRecommended;
        UseRecommendedToggle.Content = _config.UseRecommended ? "Recommended settings: ON" : "Recommended settings: OFF";
        AutoUpdateToggle.Content = _config.AutoUpdate ? "Auto Update: ON" : "Auto Update: OFF";
        ReviewBeforeBuildToggle.IsChecked = _config.ReviewBeforeBuild;
        ApplyReviewBeforeBuild();
        UpdateMissingPathsBanner();
        UpdateConsoleState();
        UpdateGpuState();
        UpdateCustomDirBoxes();
        EnsureLogTimer();

        if (!string.IsNullOrWhiteSpace(_config.PendingUpdateNotes))
        {
            ShowReleaseNoteToast(_config.PendingUpdateVersion ?? "New Version", _config.PendingUpdateNotes);
            _config.PendingUpdateNotes = null;
            _config.PendingUpdateVersion = null;
            _configService.Save(_config);
        }

        Loaded += async (_, _) => await AutoCheckUpdateAsync();

        Loaded += async (_, _) => await CheckAllRunnersHealthAsync();
        UpdateRunnerLegendAndButtons(Selected);

        // Attached after the initial selection so it does not fire during setup.
        RunnerBox.SelectionChanged += async (_, _) =>
        {
            if (Selected is { } profile)
            {
                _config.SelectedRunnerId = profile.Id;
                _configService.Save(_config);
            }
            UpdateConsoleState();
            UpdateGpuState();
            UpdateRunnerLegendAndButtons(Selected);
            await RefreshVersionAsync();
        };
        Loaded += async (_, _) => await RefreshVersionAsync();
        Closing += (_, _) => FlushLaunchOptionsSave();   // a launch-options edit made just before closing is saved
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
    // Messages are queued and written to the box in batches on a timer, so a chatty process can never
    // block on the UI thread, and the box stays small enough to remain fast.
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _logQueue = new();
    private System.Windows.Threading.DispatcherTimer? _logTimer;
    private const int MaxLogChars = 200_000;

    private void Log(string msg)
    {
        _logQueue.Enqueue(msg);
        if (_logTimer == null) Dispatcher.BeginInvoke(new Action(EnsureLogTimer));
    }

    private void EnsureLogTimer()
    {
        if (_logTimer != null) return;
        _logTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _logTimer.Tick += (_, _) => FlushLog();
        _logTimer.Start();
    }

    private void FlushLog()
    {
        if (_logQueue.IsEmpty) return;
        var sb = new System.Text.StringBuilder();
        var n = 0;
        while (n < 2000 && _logQueue.TryDequeue(out var line)) { sb.AppendLine(line); n++; }
        LogBox.AppendText(sb.ToString());
        if (LogBox.Text.Length > MaxLogChars)
        {
            var text = LogBox.Text;
            var cut = text.IndexOf('\n', text.Length - MaxLogChars / 2);
            LogBox.Text = cut >= 0 ? text[(cut + 1)..] : text[^(MaxLogChars / 2)..];
        }
        LogBox.ScrollToEnd();
    }

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

    /// <summary>"amd", "nvidia", or "off": the graphics card whose recommended settings are used for the selected runner.</summary>
    private string SelectedGpu => Selected == null ? "amd" :
        _config.RunnerGpu.TryGetValue(Selected.Id, out var g) && (g == "amd" || g == "nvidia" || g == "off") ? g : Selected.DefaultGpu;

    private void UpdateGpuState()
    {
        if (GpuButton == null) return;
        GpuButton.IsEnabled = Selected != null;
        GpuButton.Content = SelectedGpu switch
        {
            "nvidia" => "GPU: Nvidia",
            "off" => "GPU: Off",
            _ => "GPU: AMD"
        };
        GpuButton.ToolTip = "Choose your graphics card (AMD, Nvidia, or Off). When Off, recommended settings are disabled.";
        UpdateRecommendedText();
    }

    private void Gpu_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        _config.RunnerGpu[Selected.Id] = SelectedGpu switch
        {
            "amd" => "nvidia",
            "nvidia" => "off",
            _ => "amd"
        };
        _configService.Save(_config);
        UpdateGpuState();
    }

    private void UseRecommended_Changed(object sender, RoutedEventArgs e)
    {
        if (UseRecommendedToggle == null) return;
        _config.UseRecommended = UseRecommendedToggle.IsChecked == true;
        UseRecommendedToggle.Content = _config.UseRecommended ? "Recommended settings: ON" : "Recommended settings: OFF";
        _configService.Save(_config);
    }

    /// <summary>Settings > Recommended Settings: what the selected runner's project recommends, and what each setting does.</summary>
    private void UpdateRecommendedText()
    {
        if (RecommendedTitleText == null || RecommendedBodyText == null) return;
        if (Selected is not { } profile)
        {
            RecommendedTitleText.Text = "";
            RecommendedBodyText.Text = "";
            return;
        }

        var gpu = SelectedGpu;
        var gpuLabel = gpu switch { "nvidia" => "Nvidia", "off" => "Off", _ => "AMD" };
        RecommendedTitleText.Text = $"{profile.Name} — {gpuLabel}";
        if (string.Equals(profile.Id, "nvidia", StringComparison.OrdinalIgnoreCase))
        {
            RecommendedBodyText.Text = "Recommended settings are disabled for Nvidia - VIctory Based (the runner manages its own settings).";
            return;
        }
        if (gpu == "off")
        {
            RecommendedBodyText.Text = "GPU is set to Off — recommended settings are disabled.";
            return;
        }
        var preset = profile.PresetFor(gpu);
        if (preset == null)
        {
            RecommendedBodyText.Text = "This runner has no recommended settings.";
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(preset.Summary);
        if (!string.IsNullOrWhiteSpace(preset.Args)) sb.AppendLine().AppendLine($"Arguments: {preset.Args}");
        if (preset.Env.Count == 0)
        {
            sb.AppendLine().Append("Environment variables: none.");
        }
        else
        {
            sb.AppendLine().AppendLine("Environment variables:");
            foreach (var e in preset.Env)
                sb.AppendLine($"  {e.Name} = {e.Value}").AppendLine($"      {e.Description}");
        }
        RecommendedBodyText.Text = sb.ToString().TrimEnd();
    }

    private void CustomRunnerUrlSave_Click(object sender, RoutedEventArgs e)
    {
        var url = (CustomRunnerUrlBox?.Text ?? "").Trim().TrimEnd('/');
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            url = url[..^4];

        _config.CustomRunnerGitUrl = url;
        _configService.Save(_config);

        var custom = _profiles.FirstOrDefault(p => string.Equals(p.Id, "custom", StringComparison.OrdinalIgnoreCase));
        if (custom != null) custom.GitUrl = url;

        if (CustomRunnerUrlStatusText != null)
        {
            CustomRunnerUrlStatusText.Text = string.IsNullOrWhiteSpace(url)
                ? "Cleared. Custom Runner buttons are disabled until a GitHub URL is set."
                : $"Saved: {url}";
            CustomRunnerUrlStatusText.Foreground = string.IsNullOrWhiteSpace(url) ? Brushes.Orange : Brushes.LimeGreen;
        }

        UpdateRunnerLegendAndButtons(Selected);
    }

    private void UpdateCustomDirBoxes()
    {
        if (CustomRunnersDirBox != null)
            CustomRunnersDirBox.Text = string.IsNullOrWhiteSpace(_config.CustomRunnersDir) ? Paths.Root : _config.CustomRunnersDir;
        if (CustomCacheDirBox != null)
            CustomCacheDirBox.Text = string.IsNullOrWhiteSpace(_config.CustomCacheDir) ? RunnerService.PipelineCacheDir : _config.CustomCacheDir;
    }

    private void BrowseCustomRunnersDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Custom Runners Directory" };
        if (dlg.ShowDialog() == true)
        {
            _config.CustomRunnersDir = dlg.FolderName;
            RunnerService.CustomRunnersRoot = dlg.FolderName;
            _configService.Save(_config);
            UpdateCustomDirBoxes();
            RefreshStorage_Click(this, new RoutedEventArgs());
            _ = CheckAllRunnersHealthAsync();
            _ = RefreshVersionAsync();
        }
    }

    private void ResetCustomRunnersDir_Click(object sender, RoutedEventArgs e)
    {
        _config.CustomRunnersDir = "";
        RunnerService.CustomRunnersRoot = "";
        _configService.Save(_config);
        UpdateCustomDirBoxes();
        RefreshStorage_Click(this, new RoutedEventArgs());
        _ = CheckAllRunnersHealthAsync();
        _ = RefreshVersionAsync();
    }

    private void BrowseCustomCacheDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Custom Pipeline / Shader Cache Directory" };
        if (dlg.ShowDialog() == true)
        {
            _config.CustomCacheDir = dlg.FolderName;
            RunnerService.CustomCacheRoot = dlg.FolderName;
            _configService.Save(_config);
            UpdateCustomDirBoxes();
            RefreshStorage_Click(this, new RoutedEventArgs());
        }
    }

    private void ResetCustomCacheDir_Click(object sender, RoutedEventArgs e)
    {
        _config.CustomCacheDir = "";
        RunnerService.CustomCacheRoot = "";
        _configService.Save(_config);
        UpdateCustomDirBoxes();
        RefreshStorage_Click(this, new RoutedEventArgs());
    }

    private void CoolStart_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Cool Start will delete all installed runners (Victory / XCL), pipeline/shader cache, and reset Americas Launcher local files.\n\nAre you sure you want to start fresh?",
            "Cool Start (Clean Reset)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] dirsToDelete =
            [
                Paths.Root,
                RunnerService.EffectiveRunnersRoot,
                RunnerService.PipelineCacheDir,
                Path.Combine(localAppData, "victory"),
                Path.Combine(localAppData, "xcl")
            ];

            foreach (var dir in dirsToDelete.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(dir))
                    RunnerService.DeleteDirectorySafe(dir);
            }

            _config.InstalledVersions.Clear();
            _config.CustomRunnersDir = "";
            _config.CustomCacheDir = "";
            RunnerService.CustomRunnersRoot = "";
            RunnerService.CustomCacheRoot = "";
            _configService.Save(_config);

            UpdateCustomDirBoxes();
            RefreshStorage_Click(this, new RoutedEventArgs());
            _ = CheckAllRunnersHealthAsync();
            _ = RefreshVersionAsync();
            MessageBox.Show("Cool Start complete. All runners and local files have been reset.", "Cool Start", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Cool Start failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string SelectedConsoleMode
    {
        get
        {
            if (Selected == null) return "x";
            if (_config.ConsoleMode.TryGetValue(Selected.Id, out var m) && (m == "x" || m == "s" || m == "off"))
                return m;
            if (_config.SeriesS.TryGetValue(Selected.Id, out var s) && s)
                return "s";
            return "x";
        }
    }

    private void UpdateConsoleState()
    {
        if (ConsoleButton == null) return;
        var supported = Selected is { SupportsConsole: true };
        ConsoleButton.IsEnabled = supported;
        ConsoleButton.Content = !supported ? "Console: N/A" : SelectedConsoleMode switch
        {
            "s" => "Xbox Series S",
            "off" => "Console: OFF",
            _ => "Xbox Series X"
        };
        ConsoleButton.ToolTip = supported
            ? "Click to cycle between Xbox Series X, Xbox Series S, and OFF for this runner."
            : "This runner has no console selection.";
    }

    private void Console_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { SupportsConsole: true } profile) return;
        _config.ConsoleMode[profile.Id] = SelectedConsoleMode switch
        {
            "x" => "s",
            "s" => "off",
            _ => "x"
        };
        _configService.Save(_config);
        UpdateConsoleState();
    }

    private void SetUpdateIndicator(bool available)
    {
        var v = available ? Visibility.Visible : Visibility.Collapsed;
        TitleUpdateText.Visibility = v;
        SettingsUpdateDot.Visibility = v;
    }

    // ---- Nvidia - VIctory Based page ----------------------------------------------------------------------------

    private static bool IsNvidiaProfile(RunnerProfile p) => string.Equals(p.Id, "nvidia", StringComparison.OrdinalIgnoreCase);

    private RunnerProfile? NvidiaProfile => _profiles.FirstOrDefault(IsNvidiaProfile);

    private string? NvidiaDir => NvidiaProfile is { } p ? RunnerService.SourceDir(p) : null;

    private bool NvidiaInstalled => NvidiaProfile is { } p && RunnerService.HasSource(p);

    private bool _nvidiaBusy;

    private void UpdateNvidiaState()
    {
        if (NvidiaStatusText == null) return;
        var installed = NvidiaInstalled;
        var dir = NvidiaDir;
        NvidiaStatusText.Text = NvidiaProfile == null
            ? "The Nvidia runner is missing from runners.json."
            : installed ? $"Installed: {dir}" : "Not installed yet. Click Install first.";
        NvidiaInstallButton.IsEnabled = !_nvidiaBusy && NvidiaProfile != null;
        foreach (var b in new[] { NvidiaSetGameButton, NvidiaFsrButton, NvidiaDlssButton, NvidiaCheckButton })
            b.IsEnabled = installed && !_nvidiaBusy;
        // The Play buttons need one click on "Set Game folder and exe" first; once done they stay open (even after updates).
        foreach (var b in new[] { NvidiaFramegenButton, NvidiaPlayButton })
        {
            b.IsEnabled = installed && !_nvidiaBusy && _config.NvidiaGameSet;
            b.ToolTip = _config.NvidiaGameSet ? b.ToolTip : "Click \"Set Game folder and exe\" first.";
        }
    }

    private async void NvidiaCheck_Click(object sender, RoutedEventArgs e)
    {
        if (NvidiaProfile is not { } profile) return;
        _nvidiaBusy = true;
        UpdateNvidiaState();
        NvidiaStatusText.Text = "Checking for updates...";
        var st = await _runners.CheckForUpdateAsync(profile, default, sourceOnly: true);
        _nvidiaBusy = false;
        UpdateNvidiaState();
        NvidiaStatusText.Text = st switch
        {
            UpdateStatus.UpToDate => "Up to date.",
            UpdateStatus.UpdateAvailable => "An update is available. Click Install to update.",
            UpdateStatus.NotInstalled => "Not installed yet. Click Install first.",
            _ => "Could not check (offline or git missing)."
        };
    }

    private async void NvidiaInstall_Click(object sender, RoutedEventArgs e)
    {
        if (NvidiaProfile is not { } profile) return;
        _nvidiaBusy = true;
        UpdateNvidiaState();
        NvidiaStatusText.Text = "Downloading the runner with git...";
        try
        {
            var ok = await _runners.UpdateSourceAsync(profile, line =>
            {
                var text = line.Trim();
                if (text.Length > 0) Dispatcher.BeginInvoke(new Action(() => NvidiaStatusText.Text = text));
            }, reviewStep: false);
            _nvidiaBusy = false;
            UpdateNvidiaState();
            if (!ok) NvidiaStatusText.Text = "Install did not finish. Is git installed? Try again.";
        }
        catch (Exception ex)
        {
            _nvidiaBusy = false;
            UpdateNvidiaState();
            NvidiaStatusText.Text = "Install failed: " + ex.Message;
        }
    }

    /// <summary>Opens victory.toml in the runner's folder, creating it from victory.example.toml first if needed.</summary>
    private void NvidiaSetGame_Click(object sender, RoutedEventArgs e)
    {
        var dir = NvidiaDir;
        if (dir == null || !NvidiaInstalled) return;
        MessageBox.Show(
            "You do not need to set game_dir = 'C:\\gamefile' literally. Set the location of the folder the game is in (not the exe).\n\n" +
            "Example: game_dir = 'D:\\Games\\MyGame'",
            "Set Game folder and exe", MessageBoxButton.OK, MessageBoxImage.Information);
        try
        {
            var toml = Path.Combine(dir, "victory.toml");
            if (!File.Exists(toml))
            {
                var example = Path.Combine(dir, "victory.example.toml");
                if (!File.Exists(example))
                {
                    MessageBox.Show("victory.example.toml was not found in the runner's folder:\n" + dir,
                        "Nvidia - VIctory Based", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                File.Copy(example, toml);
            }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{toml}\"") { UseShellExecute = true });
            _config.NvidiaGameSet = true;
            _configService.Save(_config);
            UpdateNvidiaState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Nvidia - VIctory Based", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NvidiaFsr_Click(object sender, RoutedEventArgs e) => RunNvidiaScript("Get-AMD-FSR.cmd");
    private void NvidiaDlss_Click(object sender, RoutedEventArgs e) => RunNvidiaScript("Get-NVIDIA-DLSS.cmd");
    private void NvidiaFramegen_Click(object sender, RoutedEventArgs e) => RunNvidiaScript("PLAY_FRAMEGEN.cmd");
    private void NvidiaPlay_Click(object sender, RoutedEventArgs e) => RunNvidiaScript("Play-VIctory.cmd");

    /// <summary>Runs one of the runner's own .cmd files in its own command window (its output is not written to the launcher log).</summary>
    private void RunNvidiaScript(string name)
    {
        var dir = NvidiaDir;
        if (dir == null || !NvidiaInstalled) return;
        var script = Path.Combine(dir, name);
        if (!File.Exists(script))
        {
            MessageBox.Show($"{name} was not found in the runner's folder. Click Install to update it.\n{dir}",
                "Nvidia - VIctory Based", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/k \"\"{script}\"\"",   // /k: the window stays open so messages can be read
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            // Play-VIctory.cmd looks for the game in VICTORY_GAME_DIR: use the launcher's Game Folder when it is set.
            if (name.Equals("Play-VIctory.cmd", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(_config.GameFolder) &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VICTORY_GAME_DIR")))
                psi.Environment["VICTORY_GAME_DIR"] = _config.GameFolder.TrimEnd('\\', '/');
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Nvidia - VIctory Based", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (LaunchView == null || PortStatusView == null || RunnersView == null || SettingsView == null || NvidiaView == null) return;
        FlushLaunchOptionsSave();   // an edit made just before leaving Settings is saved
        LaunchView.Visibility = LaunchNavButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        NvidiaView.Visibility = NvidiaNavButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (NvidiaNavButton.IsChecked == true) UpdateNvidiaState();
        PortStatusView.Visibility = PortStatusNavButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RunnersView.Visibility = RunnersNavButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = SettingsNavButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (PortStatusNavButton.IsChecked == true && !_portLoadedOnce)
        {
            _portLoadedOnce = true;
            await LoadPortStatusAsync();
        }

        if (SettingsNavButton.IsChecked == true)
        {
            LoadLaunchOptionsEditor();
            await RefreshStorageAsync();
        }
    }

    // Settings > Launch Options: the runner shown in the editor and its variable rows. Changes save automatically.
    private readonly LaunchOptionsStore _launchStore;
    private RunnerProfile? _launchOptionsRunner;   // the runner whose rows the editor shows
    private RunnerProfile? _launchOptionsTarget;   // set by a card's gear: show this one next
    private readonly System.Collections.ObjectModel.ObservableCollection<EnvVarRow> _envRows = new();
    private bool _loadingLaunchOptions;
    private DispatcherTimer? _launchOptionsSaveTimer;

    /// <summary>
    /// Fills the runner dropdown with the installed runners and shows one runner's settings: the one already
    /// shown if it is still installed, else the runner selected on the Launch page, else the first installed one.
    /// </summary>
    private void LoadLaunchOptionsEditor()
    {
        FlushLaunchOptionsSave();
        LaunchEnvRows.ItemsSource = _envRows;

        var installed = _launchProfiles.Where(p => _runners.IsInstalled(p)).ToList();
        var requested = _launchOptionsTarget;
        _launchOptionsTarget = null;
        var target = installed.FirstOrDefault(p => p.Id == requested?.Id)
                     ?? installed.FirstOrDefault(p => p.Id == _launchOptionsRunner?.Id)
                     ?? installed.FirstOrDefault(p => p.Id == Selected?.Id)
                     ?? installed.FirstOrDefault();

        _loadingLaunchOptions = true;
        LaunchOptionsRunnerBox.ItemsSource = installed;
        LaunchOptionsRunnerBox.SelectedItem = target;
        _loadingLaunchOptions = false;

        var enabled = target != null;
        LaunchOptionsRunnerBox.IsEnabled = LaunchOptionsArgsBox.IsEnabled = enabled;
        AddEnvRowButton.IsEnabled = PasteEnvListButton.IsEnabled = RemoveAllEnvRowsButton.IsEnabled = enabled;

        if (target == null)
        {
            _launchOptionsRunner = null;
            _loadingLaunchOptions = true;
            ClearEnvRows();
            LaunchOptionsArgsBox.Text = "";
            _loadingLaunchOptions = false;
            UpdateEnvRowsEmptyText();
            SetLaunchOptionsStatus("No runners are installed yet. Install one on the Launch page first.", Brushes.LightSlateGray);
            return;
        }

        ShowLaunchOptions(target);
    }

    /// <summary>Loads one runner's saved launch options into the editor.</summary>
    private void ShowLaunchOptions(RunnerProfile profile)
    {
        _launchOptionsRunner = profile;
        var options = _launchStore.Get(profile.Id);

        _loadingLaunchOptions = true;
        ClearEnvRows();
        if (options != null)
            foreach (var (name, value) in options.Env) AddEnvRow(name, value);
        LaunchOptionsArgsBox.Text = options?.ExtraArgs ?? "";
        _loadingLaunchOptions = false;

        UpdateEnvRowsEmptyText();
        if (_launchStore.LoadProblem is { } problem)
            SetLaunchOptionsStatus(problem, Brushes.Orange);
        else
            SetLaunchOptionsStatus(DescribeLaunchOptions(options), Brushes.LightSlateGray);
    }

    /// <summary>The gear on a runner card: opens Settings with that runner's launch options (or Custom Runner settings).</summary>
    private void OpenRunnerSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: RunnerProfile profile }) return;
        e.Handled = true;

        if (string.Equals(profile.Id, "custom", StringComparison.OrdinalIgnoreCase))
        {
            if (SettingsNavButton.IsChecked != true) SettingsNavButton.IsChecked = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => CustomRunnerCard.BringIntoView());
            return;
        }

        if (!_runners.IsInstalled(profile))
        {
            Log($"{profile.Name} is not installed yet. Install it to give it launch options.");
            return;
        }

        FlushLaunchOptionsSave();
        _launchOptionsTarget = profile;   // the editor switches to it when it next loads

        if (SettingsNavButton.IsChecked == true) LoadLaunchOptionsEditor();
        else SettingsNavButton.IsChecked = true;   // Nav_Checked loads the editor
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => LaunchOptionsCard.BringIntoView());
    }

    private void LaunchOptionsRunner_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingLaunchOptions || LaunchOptionsRunnerBox.SelectedItem is not RunnerProfile profile) return;
        if (profile.Id == _launchOptionsRunner?.Id) return;
        FlushLaunchOptionsSave();   // the previous runner's last edit is saved before switching
        ShowLaunchOptions(profile);
    }

    private EnvVarRow AddEnvRow(string name = "", string value = "")
    {
        var row = new EnvVarRow { Name = name, Value = value };
        row.PropertyChanged += EnvRow_PropertyChanged;
        _envRows.Add(row);
        UpdateEnvRowsEmptyText();
        return row;
    }

    private void ClearEnvRows()
    {
        foreach (var row in _envRows) row.PropertyChanged -= EnvRow_PropertyChanged;
        _envRows.Clear();
    }

    private void UpdateEnvRowsEmptyText()
    {
        var empty = _envRows.Count == 0;
        LaunchEnvEmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        LaunchEnvHeader.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void EnvRow_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EnvVarRow.Name) or nameof(EnvVarRow.Value)) ScheduleLaunchOptionsSave();
    }

    private void LaunchOptionsArgs_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ScheduleLaunchOptionsSave();

    private void AddEnvRow_Click(object sender, RoutedEventArgs e) => AddEnvRow();

    private void RemoveEnvRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: EnvVarRow row }) return;
        row.PropertyChanged -= EnvRow_PropertyChanged;
        _envRows.Remove(row);
        UpdateEnvRowsEmptyText();
        ScheduleLaunchOptionsSave();
    }

    private void RemoveAllEnvRows_Click(object sender, RoutedEventArgs e)
    {
        if (_launchOptionsRunner is not { } profile || _envRows.Count == 0) return;
        var answer = MessageBox.Show(this, $"Remove all {_envRows.Count} environment variable(s) for {profile.Name}?",
            "Remove all variables", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        ClearEnvRows();
        UpdateEnvRowsEmptyText();
        ScheduleLaunchOptionsSave();
    }

    /// <summary>Adds NAME=value lines from the clipboard; a name that already has a row gets the new value.</summary>
    private void PasteEnvList_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try { text = Clipboard.GetText(); }
        catch { text = ""; }

        var env = LaunchOptionsService.ParseEnv(text, out var errors);
        if (env.Count == 0)
        {
            SetLaunchOptionsStatus("Nothing to paste: copy lines like NAME=value first.", Brushes.IndianRed);
            return;
        }

        // Drop empty rows left from "Add variable" so the pasted list is not split by blanks.
        foreach (var blank in _envRows.Where(r => r.Name.Trim() == "" && r.Value.Trim() == "").ToList())
        {
            blank.PropertyChanged -= EnvRow_PropertyChanged;
            _envRows.Remove(blank);
        }

        foreach (var (name, value) in env)
        {
            var existing = _envRows.FirstOrDefault(r => string.Equals(r.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) existing.Value = value;
            else AddEnvRow(name, value);
        }

        UpdateEnvRowsEmptyText();
        FlushLaunchOptionsSave(force: true);
        if (errors.Count > 0)
            SetLaunchOptionsStatus(LaunchOptionsStatusText.Text + $" Skipped {errors.Count} line(s). {errors[0]}", Brushes.Orange);
    }

    /// <summary>Copies the variables as NAME=value lines (what Paste list reads), plus the extra arguments as a comment.</summary>
    private void CopyEnvList_Click(object sender, RoutedEventArgs e)
    {
        var rows = _envRows.Where(r => r.Name.Trim() != "").ToList();
        var args = LaunchOptionsArgsBox.Text.Trim();
        if (rows.Count == 0 && args == "")
        {
            SetLaunchOptionsStatus("Nothing to copy yet.", Brushes.LightSlateGray);
            return;
        }

        var sb = new System.Text.StringBuilder();
        if (_launchOptionsRunner != null) sb.Append("# ").Append(_launchOptionsRunner.Name).AppendLine(" launch options");
        if (args != "") sb.Append("# Extra arguments: ").AppendLine(args);
        foreach (var row in rows) sb.Append(row.Name.Trim()).Append('=').AppendLine(row.Value);

        try
        {
            Clipboard.SetText(sb.ToString());
            SetLaunchOptionsStatus($"Copied {rows.Count} variable(s) to the clipboard.", Brushes.LimeGreen);
        }
        catch (Exception ex)
        {
            SetLaunchOptionsStatus("Could not copy: " + ex.Message, Brushes.IndianRed);
        }
    }

    /// <summary>Saves shortly after the last change, so typing does not write the file on every key.</summary>
    private void ScheduleLaunchOptionsSave()
    {
        if (_loadingLaunchOptions || _launchOptionsRunner == null) return;
        if (_launchOptionsSaveTimer == null)
        {
            _launchOptionsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _launchOptionsSaveTimer.Tick += (_, _) => FlushLaunchOptionsSave(force: true);
        }
        _launchOptionsSaveTimer.Stop();
        _launchOptionsSaveTimer.Start();
    }

    /// <summary>Saves now if a save is waiting (or always, with <paramref name="force"/>).</summary>
    private void FlushLaunchOptionsSave(bool force = false)
    {
        var pending = _launchOptionsSaveTimer?.IsEnabled == true;
        _launchOptionsSaveTimer?.Stop();
        if (pending || force) SaveLaunchOptions();
    }

    /// <summary>
    /// Saves the rows that are valid. A row with a missing, invalid or repeated name is marked and left out,
    /// and the status line says why. A fully empty row is ignored.
    /// </summary>
    private void SaveLaunchOptions()
    {
        if (_launchOptionsRunner is not { } profile) return;

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problems = 0;
        string? firstProblem = null;
        for (var i = 0; i < _envRows.Count; i++)
        {
            var row = _envRows[i];
            var name = row.Name.Trim();
            var value = row.Value;   // used exactly as typed: spaces can matter in a value

            if (name == "" && value.Trim() == "") { row.Error = null; continue; }
            if (name == "") row.Error = "Add a name.";
            else if (!LaunchOptionsService.IsValidName(name)) row.Error = "A name cannot contain =.";
            else if (env.ContainsKey(name)) row.Error = "This name is already in a row above.";
            else if (LaunchOptionsService.CheckValue(value) is { } valueProblem) row.Error = valueProblem;
            else { env[name] = value; row.Error = null; continue; }

            problems++;
            firstProblem ??= $"Row {i + 1}: {row.Error}";
        }

        var options = new RunnerLaunchOptions { Env = env, ExtraArgs = LaunchOptionsArgsBox.Text.Trim() };
        _launchStore.Set(profile.Id, options);
        if (_launchStore.Save() is { } saveError)
        {
            SetLaunchOptionsStatus("Not saved: " + saveError, Brushes.IndianRed);
            return;
        }

        var saved = "Saved. " + DescribeLaunchOptions(options);
        if (_launchStore.TakeNotice() is { } notice)
            SetLaunchOptionsStatus($"{saved} {notice}", Brushes.Orange);
        else if (problems > 0)
            SetLaunchOptionsStatus($"{saved} {problems} row(s) skipped. {firstProblem}", Brushes.Orange);
        else
            SetLaunchOptionsStatus(saved, Brushes.LimeGreen);
    }

    private static string DescribeLaunchOptions(RunnerLaunchOptions? options)
    {
        if (options is not { IsEmpty: false }) return "No launch options. It starts normally.";
        var text = $"{options.Env.Count} variable(s)";
        if (!string.IsNullOrWhiteSpace(options.ExtraArgs)) text += $", arguments: {options.ExtraArgs.Trim()}";
        return text + ".";
    }

    private void SetLaunchOptionsStatus(string text, Brush color)
    {
        LaunchOptionsStatusText.Text = text;
        LaunchOptionsStatusText.Foreground = color;
    }

    private sealed record RunnerStorageItem(RunnerProfile Profile, string Name, string SizeText, bool CanDelete);

    private async void RefreshStorage_Click(object sender, RoutedEventArgs e) => await RefreshStorageAsync();

    private async Task RefreshStorageAsync()
    {
        var cacheDir = RunnerService.PipelineCacheDir;
        PipelineCachePathText.Text = cacheDir;

        var (cacheBytes, items) = await Task.Run(() =>
        {
            var cBytes = RunnerService.GetDirectorySize(cacheDir);
            var list = _profiles.Select(p =>
            {
                var dir = RunnerService.SourceDir(p);
                var bytes = RunnerService.GetDirectorySize(dir);
                var exists = Directory.Exists(dir);
                return new RunnerStorageItem(
                    p,
                    p.Name,
                    exists ? RunnerService.FormatBytes(bytes) : "Not installed",
                    exists);
            }).ToList();
            return (cBytes, list);
        });

        PipelineCacheSizeText.Text = Directory.Exists(cacheDir) ? RunnerService.FormatBytes(cacheBytes) : "0 B";
        DeletePipelineCacheButton.IsEnabled = Directory.Exists(cacheDir);
        RunnerStorageList.ItemsSource = items;
    }

    private async void DeletePipelineCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Run(() => RunnerService.DeleteDirectorySafe(RunnerService.PipelineCacheDir));
            await RefreshStorageAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to delete pipeline cache: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteRunner_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: RunnerProfile profile }) return;
        try
        {
            await Task.Run(() => RunnerService.DeleteDirectorySafe(RunnerService.SourceDir(profile)));
            _config.InstalledVersions.Remove(profile.Id);
            _configService.Save(_config);

            // Show it as not installed right away: card colour, its launch-options gear, and the Launch Options list.
            SyncCardInstallState(profile, installed: false);
            LoadLaunchOptionsEditor();

            await RefreshStorageAsync();
            await RefreshVersionAsync();
            await CheckAllRunnersHealthAsync();   // also refreshes anything else that counts installed runners
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete {profile.Name}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            SetUpdateIndicator(res.IsUpdateAvailable);
            LauncherUpdateStatusText.Text = res.Message;
            LauncherUpdateStatusText.Foreground = res.IsUpdateAvailable
                ? Brushes.Orange
                : res.HasRelease ? Brushes.LimeGreen : Brushes.LightSlateGray;

            _latestReleaseUrl = res.ReleaseUrl;
            _latestDownloadUrl = res.DownloadUrl;
            _latestVersion = res.LatestVersion;
            _latestReleaseNotes = res.ReleaseNotes;
            OpenReleaseButton.Content = !string.IsNullOrWhiteSpace(res.DownloadUrl) ? "Update Now" : "Open Release";
            OpenReleaseButton.Visibility = res.IsUpdateAvailable &&
                (!string.IsNullOrWhiteSpace(res.DownloadUrl) || !string.IsNullOrWhiteSpace(res.ReleaseUrl))
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

    private async void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_latestDownloadUrl))
        {
            CheckLauncherUpdateButton.IsEnabled = false;
            OpenReleaseButton.IsEnabled = false;
            try
            {
                _config.PendingUpdateNotes = _latestReleaseNotes;
                _config.PendingUpdateVersion = _latestVersion;
                _configService.Save(_config);

                await _launcherUpdates.DownloadAndApplyUpdateAsync(
                    _latestDownloadUrl,
                    msg => Dispatcher.Invoke(() => LauncherUpdateStatusText.Text = msg));
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                LauncherUpdateStatusText.Text = "Update failed: " + ex.Message;
                LauncherUpdateStatusText.Foreground = Brushes.IndianRed;
                CheckLauncherUpdateButton.IsEnabled = true;
                OpenReleaseButton.IsEnabled = true;
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(_latestReleaseUrl))
            Process.Start(new ProcessStartInfo(_latestReleaseUrl) { UseShellExecute = true });
    }

    private async Task AutoCheckUpdateAsync()
    {
        try
        {
            var res = await _launcherUpdates.CheckForUpdateAsync();
            SetUpdateIndicator(res.IsUpdateAvailable);
            if (res.IsUpdateAvailable) { _latestReleaseUrl = res.ReleaseUrl; _latestDownloadUrl = res.DownloadUrl; _latestVersion = res.LatestVersion; _latestReleaseNotes = res.ReleaseNotes; }
            if (_config.AutoUpdate && res.IsUpdateAvailable && !string.IsNullOrWhiteSpace(res.DownloadUrl))
            {
                _config.PendingUpdateNotes = res.ReleaseNotes;
                _config.PendingUpdateVersion = res.LatestVersion;
                _configService.Save(_config);
                await _launcherUpdates.DownloadAndApplyUpdateAsync(res.DownloadUrl, _ => { });
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            // Silent on startup, but written to %LocalAppData%\AmericasLauncher\update.log
            LauncherUpdateService.LogProblem("startup update check failed: " + ex.Message);
        }
    }

    private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = AutoUpdateToggle.IsChecked == true;
        AutoUpdateToggle.Content = enabled ? "Auto Update: ON" : "Auto Update: OFF";
        _config.AutoUpdate = enabled;
        _configService.Save(_config);
    }

    private void ReviewBeforeBuild_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = ReviewBeforeBuildToggle.IsChecked == true;
        if (_config.ReviewBeforeBuild == enabled) return;   // set at startup
        _config.ReviewBeforeBuild = enabled;
        _configService.Save(_config);
        ApplyReviewBeforeBuild();
        _ = RefreshVersionAsync();
    }

    /// <summary>
    /// Off: one "Install / Update" button (download and build). On: "Update Source" (download only)
    /// and "Build" (build the downloaded source only), so new runner code can be checked in between.
    /// </summary>
    private void ApplyReviewBeforeBuild()
    {
        var review = _config.ReviewBeforeBuild;
        ReviewBeforeBuildToggle.Content = review ? "Review Before Build: ON" : "Review Before Build: OFF";
        UpdateSourceButton.Visibility = review ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Content = review ? "⚙  Build" : "Install / Update";
        InstallButton.ToolTip = review ? "Build the source that is already downloaded. The source is not updated." : null;
        UpdateRunnerLegendAndButtons(Selected);
    }

    private void OpenRunnerGithub_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: RunnerProfile profile }) return;
        var url = (profile.GitUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("No GitHub URL is set for this runner yet.", "Runners", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            url = url[..^4];
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open browser: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowReleaseNoteToast(string version, string notes)
    {
        ToastVersionText.Text = $"Updated to {version}";
        ToastNotesText.Text = notes;
        ReleaseNoteToast.Visibility = Visibility.Visible;
        ToastCountdownText.Text = "";
        _toastTimer?.Stop();
        _toastTimer = null;
    }

    private void CloseToast()
    {
        _toastTimer?.Stop();
        _toastTimer = null;
        ReleaseNoteToast.Visibility = Visibility.Collapsed;
    }

    private void ToastClose_Click(object sender, RoutedEventArgs e) => CloseToast();

    /// <summary>
    /// Keeps a runner card in step with what is on disk after an install or delete (its colour and its
    /// launch-options gear). Only the installed / not installed change is made: any other state the
    /// startup health check set (an update, a broken repository) is left as it is.
    /// </summary>
    private void SyncCardInstallState(RunnerProfile profile, bool installed, bool updateAvailable = false, bool checkedUpstream = false)
    {
        var before = profile.HealthState;
        if (!installed && before != RunnerHealthState.NotInstalled && before != RunnerHealthState.Broken)
            profile.HealthState = RunnerHealthState.NotInstalled;
        else if (installed && before == RunnerHealthState.NotInstalled)
            profile.HealthState = updateAvailable ? RunnerHealthState.UpdateAvailable : RunnerHealthState.UpToDate;
        else if (installed && checkedUpstream && before == RunnerHealthState.UpdateAvailable && !updateAvailable)
            profile.HealthState = RunnerHealthState.UpToDate;   // it was updated: the yellow card turns green again

        if (profile.HealthState != before && Selected?.Id == profile.Id) UpdateRunnerLegendAndButtons(profile);
    }

    private async Task<bool> CanCheckRunnerUpdatesAsync()
    {
        if (_gitAvailability is null || _gitAvailability.IsCompleted)
            _gitAvailability = DependencyService.IsCommandAvailableAsync("git.exe");

        var available = await _gitAvailability;
        if (!available && !_reportedMissingGit)
            Log("[!] Git is unavailable. Install Git or add it to PATH, then reopen AmericasLauncher to check for runner updates.");

        _reportedMissingGit = !available;
        return available;
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
            var gitAvailable = await CanCheckRunnerUpdatesAsync();
            if (ticket != _refreshCounter) return;
            if (!gitAvailable)
            {
                VersionText.Text = _runners.IsInstalled(profile)
                    ? "Emulator version: " + (BuiltVersion(profile) ?? "unknown")
                    : "Emulator version: not installed";
                SetStatus("Git is unavailable", Brushes.LightSlateGray);
                return;
            }

            if (!_runners.IsInstalled(profile))
            {
                var source = await _runners.GetSourceVersionAsync(profile);
                if (ticket != _refreshCounter) return;
                if (source != "")
                {
                    VersionText.Text = $"Source: {source} · not built yet";
                    SetStatus("Downloaded, not built", Brushes.Orange);
                }
                else
                {
                    VersionText.Text = "Emulator version: not installed";
                    SetStatus("Not installed", Brushes.LightSlateGray);
                }
                SyncCardInstallState(profile, installed: false);
                return;
            }

            var version = await _runners.GetInstalledVersionAsync(profile);
            if (ticket != _refreshCounter) return;
            var built = BuiltVersion(profile);
            var needsBuild = built != null && version != "" && !string.Equals(built, version, StringComparison.OrdinalIgnoreCase);
            VersionText.Text = needsBuild
                ? $"Source: {version} · built: {built}"
                : "Emulator version: " + (version == "" ? "unknown" : version);
            SetStatus("Checking for updates...", Brushes.LightSlateGray);

            var status = await _runners.CheckForUpdateAsync(profile);
            if (ticket != _refreshCounter) return;
            SyncCardInstallState(profile, installed: true,
                updateAvailable: status == UpdateStatus.UpdateAvailable || needsBuild,
                checkedUpstream: status != UpdateStatus.Unknown);

            switch (status)
            {
                case UpdateStatus.UpToDate when needsBuild:
                    SetStatus("Source newer than build", Brushes.Orange);
                    break;
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

    /// <summary>The short revision this runner was last built from, or null if the launcher does not know.</summary>
    private string? BuiltVersion(RunnerProfile profile) =>
        _config.InstalledVersions.TryGetValue(profile.Id, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private bool IsMissingCustomUrl(RunnerProfile? profile) =>
        profile != null &&
        string.Equals(profile.Id, "custom", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(_config.CustomRunnerGitUrl);

    private void SetBusy(bool busy)
    {
        var isBroken = Selected?.HealthState == RunnerHealthState.Broken;
        var missingCustom = IsMissingCustomUrl(Selected);
        UpdateSourceButton.IsEnabled = !busy && !isBroken && !missingCustom;
        InstallButton.IsEnabled = !busy && !isBroken && !missingCustom;
        LaunchButton.IsEnabled = !busy && !missingCustom;
        RunnerBox.IsEnabled = !busy;
        Progress.IsIndeterminate = busy;
    }

    /// <summary>Switches the Launch button between "Launch Game" and "Stop Game" and locks the rest of the UI.</summary>
    private void SetLaunchUi(bool active)
    {
        var isBroken = Selected?.HealthState == RunnerHealthState.Broken;
        var missingCustom = IsMissingCustomUrl(Selected);
        LaunchButton.Content = active ? "■  Stop Game" : "▶  Launch Game";
        LaunchButton.IsEnabled = active || !missingCustom;   // while active this is the Stop button, so it must stay clickable
        UpdateSourceButton.IsEnabled = !active && !isBroken && !missingCustom;
        InstallButton.IsEnabled = !active && !isBroken && !missingCustom;
        RunnerBox.IsEnabled = !active;
        Progress.IsIndeterminate = active;
    }

    private void EndLaunch(CancellationTokenSource cts)
    {
        if (!ReferenceEquals(_launchCts, cts)) return;   // already ended
        _launchCts = null;
        _launchProfile = null;
        _runningProcess = null;
        cts.Dispose();
        SetLaunchUi(false);
    }

    /// <summary>
    /// Works at any time after Launch was clicked: while the game runs.
    /// Stops the game and any leftover process of the runner's exe (e.g. victory.exe).
    /// </summary>
    private void StopGame()
    {
        var cts = _launchCts;
        var profile = _launchProfile;
        if (cts == null) return;

        Log("Stopping...");
        LaunchButton.IsEnabled = false;   // prevent double clicks until the launch has fully ended

        try { cts.Cancel(); } catch (ObjectDisposedException) { }

        try { _runningProcess?.Kill(entireProcessTree: true); }
        catch { /* already exited */ }

        if (profile != null)
        {
            var killed = RunnerService.KillRunnerProcesses(profile);
            if (killed > 0) Log($"Closed {killed} background {profile.Name} process(es).");
        }

        Log("Stopped.");
        EndLaunch(cts);
    }

    private async Task CheckAllRunnersHealthAsync()
    {
        if (!await CanCheckRunnerUpdatesAsync()) return;

        var tasks = _profiles.Select(async p =>
        {
            var state = await _runners.CheckHealthAsync(p);
            // Source already updated but not built yet: show it like an update (yellow).
            if (state == RunnerHealthState.UpToDate && BuiltVersion(p) is { } built)
            {
                var source = await _runners.GetSourceVersionAsync(p);
                if (source != "" && !string.Equals(source, built, StringComparison.OrdinalIgnoreCase))
                    state = RunnerHealthState.UpdateAvailable;
            }
            Dispatcher.Invoke(() =>
            {
                p.HealthState = state;
                if (Selected?.Id == p.Id)
                    UpdateRunnerLegendAndButtons(p);
            });
        });
        await Task.WhenAll(tasks);
    }

    private void UpdateRunnerLegendAndButtons(RunnerProfile? profile)
    {
        if (profile == null)
        {
            RunnerColorLegendText.Text = "Please select a runner";
            UpdateSourceButton.IsEnabled = false;
            InstallButton.IsEnabled = false;
            return;
        }

        var review = _config.ReviewBeforeBuild;
        RunnerColorLegendText.Text = profile.HealthState switch
        {
            RunnerHealthState.UpToDate => "Green: Runner is installed and up to date.",
            RunnerHealthState.Broken => "Red: Runner repository was deleted or is no longer working.",
            RunnerHealthState.UpdateAvailable => review
                ? "Yellow: New source upstream (Update Source), or downloaded but not built yet (Build)."
                : "Yellow: Runner is installed, but an update is available.",
            _ => review ? "Black: Runner is not built yet." : "Black: Runner is not installed yet."
        };

        var missingCustom = IsMissingCustomUrl(profile);
        var enabled = profile.HealthState != RunnerHealthState.Broken && !Progress.IsIndeterminate && !missingCustom;
        UpdateSourceButton.IsEnabled = enabled;
        InstallButton.IsEnabled = enabled;
        LaunchButton.IsEnabled = !Progress.IsIndeterminate && !missingCustom;
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

    private async void UpdateSource_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        SetBusy(true);
        try
        {
            var ok = await _runners.UpdateSourceAsync(profile, Log);
            _config.SelectedRunnerId = profile.Id;
            _configService.Save(_config);
            if (!ok) Log("Source update did not finish. Check the messages above.");

            await RefreshVersionAsync();
            await CheckAllRunnersHealthAsync();
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

    /// <summary>"Install / Update" (download and build), or "Build" (build only) with Review Before Build on.</summary>
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        SetBusy(true);
        try
        {
            var ok = _config.ReviewBeforeBuild
                ? await _runners.BuildAsync(profile, _config, Log)
                : await _runners.InstallOrUpdateAsync(profile, _config, Log);
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
            await CheckAllRunnersHealthAsync();   // recolours the cards (yellow -> green after an update)
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
        // The button is "Stop Game" from the first moment of the launch.
        if (_launchCts != null)
        {
            StopGame();
            return;
        }

        if (Selected is not { } profile) return;
        FlushLaunchOptionsSave();

        if (!_runners.IsInstalled(profile))
        {
            Log(_config.ReviewBeforeBuild ? "Click 'Update Source', then 'Build' first." : "Click 'Install / Update' first.");
            return;
        }
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

        // Tell the player what changed before the game starts; Cancel keeps the game from starting.
        var notice = MessageBox.Show(
            "Before the game starts:\n\n" +
            "• The Pipeline option no longer exists. The game already collects its cache while you play. " +
            "By the second or third time you play, the pipelines are built.\n\n" +
            "• The Xbox Series S / X option does not work with some runners. If you get a black screen or a crash, " +
            "try the other Xbox Series version.\n\n" +
            "• Using each runner's recommended settings is advised. You can see the recommended settings and what " +
            "they do in Settings.",
            "Before you launch", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (notice != MessageBoxResult.OK) return;

        var cts = new CancellationTokenSource();
        _launchCts = cts;
        _launchProfile = profile;
        SetLaunchUi(true);

        try
        {
            var options = _launchStore.Get(profile.Id);
            var process = await _launcher.Launch(
                profile, _config.GameFolder, _config.GameExe, Log,
                onExited: () =>
                {
                    try { Dispatcher.Invoke(() => EndLaunch(cts)); }
                    catch { /* the window is closing */ }
                },
                ct: cts.Token,
                options: options,
                consoleMode: SelectedConsoleMode,
                gpu: SelectedGpu,
                useRecommended: _config.UseRecommended);

            // Stop was clicked just as the game process was being started: close it right away.
            if (!ReferenceEquals(_launchCts, cts))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
                return;
            }

            _runningProcess = process;
            Log("Started.");
        }
        catch (OperationCanceledException)
        {
            // Stop Game: StopGame() has already cleaned up and logged.
            EndLaunch(cts);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_launchCts, cts)) Log("ERROR: " + ex.Message);
            EndLaunch(cts);
        }
    }

    private Action<string>? _sendLogInput;

    private void SetLogInputSender(Action<string>? sender)
    {
        _sendLogInput = sender;
        Dispatcher.Invoke(() =>
        {
            if (LogInputPanel != null)
                LogInputPanel.Visibility = sender != null ? Visibility.Visible : Visibility.Collapsed;
            if (sender != null)
                LogInputBox?.Focus();
        });
    }

    private void LogInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            SendCurrentLogInput();
        }
    }

    private void LogInputSend_Click(object sender, RoutedEventArgs e) => SendCurrentLogInput();

    private void SendCurrentLogInput()
    {
        if (LogInputBox == null || _sendLogInput == null) return;
        var text = LogInputBox.Text;
        LogInputBox.Clear();
        Log($"> {text}");
        _sendLogInput(text);
    }

}
