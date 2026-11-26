using System.Diagnostics;
using System.Text.Json;
using AmericasLauncher.Linux.Models;

namespace AmericasLauncher.Linux.Services;

public static class LinuxPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AmericasLauncher");

    public static string ConfigFile => Path.Combine(Root, "config.json");
}

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(LinuxPaths.ConfigFile))
            {
                var json = File.ReadAllText(LinuxPaths.ConfigFile);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { /* fallback */ }
        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LinuxPaths.ConfigFile)!);
        File.WriteAllText(LinuxPaths.ConfigFile, JsonSerializer.Serialize(config, JsonOpts));
    }

    public static List<RunnerProfile> LoadProfiles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "runners.json");
        if (!File.Exists(path)) return new();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<RunnerProfile>>(json) ?? new();
    }
}

public static class ProcessRunner
{
    public static async Task<int> RunAsync(
        string fileName, string arguments, string? workingDirectory,
        Action<string> log, CancellationToken ct = default,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (env != null)
        {
            foreach (var kv in env)
            {
                if (kv.Value == null) psi.Environment.Remove(kv.Key);
                else psi.Environment[kv.Key] = kv.Value;
            }
        }

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };

        if (!process.Start()) return -1;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}

public sealed class LinuxRunnerService
{
    public static string CustomRunnersRoot { get; set; } = "";
    public static string CustomCacheRoot { get; set; } = "";

    public static string EffectiveRunnersRoot =>
        !string.IsNullOrWhiteSpace(CustomRunnersRoot) ? CustomRunnersRoot : LinuxPaths.Root;

    public static string SourceDir(RunnerProfile p) => Path.Combine(EffectiveRunnersRoot, "sources", p.Id);

    public static string ExePath(RunnerProfile p)
    {
        var rel = p.Exe.Replace('\\', '/');
        var winPath = Path.Combine(SourceDir(p), rel);
        if (File.Exists(winPath)) return winPath;
        var linuxPath = winPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? winPath[..^4]
            : winPath;
        return File.Exists(linuxPath) ? linuxPath : winPath;
    }

    public static string PipelineCacheDir =>
        !string.IsNullOrWhiteSpace(CustomCacheRoot)
            ? CustomCacheRoot
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "victory",
                "cache");

    public static bool IsInstalled(RunnerProfile p) => File.Exists(ExePath(p));

    public async Task<RunnerHealthState> CheckHealthAsync(RunnerProfile p, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            var lines = new List<string>();
            var code = await ProcessRunner.RunAsync(
                "git", $"ls-remote \"{p.GitUrl}\" HEAD", null,
                s => { lock (lines) lines.Add(s); }, cts.Token);

            if (code != 0) return RunnerHealthState.Broken;
            if (!IsInstalled(p)) return RunnerHealthState.NotInstalled;

            var local = await GetHeadAsync(SourceDir(p), cts.Token, full: true);
            string remote;
            lock (lines)
            {
                remote = lines.Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length >= 40 && l[..40].All(Uri.IsHexDigit))?[..40] ?? "";
            }

            if (local != "" && remote != "" && !string.Equals(local, remote, StringComparison.OrdinalIgnoreCase))
                return RunnerHealthState.UpdateAvailable;

            return RunnerHealthState.UpToDate;
        }
        catch
        {
            return IsInstalled(p) ? RunnerHealthState.UpToDate : RunnerHealthState.NotInstalled;
        }
    }

    public async Task<bool> InstallOrUpdateAsync(RunnerProfile p, AppConfig config, Action<string> log, CancellationToken ct = default)
    {
        var dir = SourceDir(p);
        Directory.CreateDirectory(Path.GetDirectoryName(dir)!);

        if (!Directory.Exists(Path.Combine(dir, ".git")))
        {
            log($"Cloning {p.GitUrl}...");
            var code = await ProcessRunner.RunAsync("git", $"clone \"{p.GitUrl}\" \"{dir}\"", null, log, ct);
            if (code != 0) return false;
        }
        else
        {
            log($"Updating {p.Name}...");
            var code = await ProcessRunner.RunAsync("git", "pull --ff-only", dir, log, ct);
            if (code != 0) return false;
        }

        log("=== Building on Linux (this can take several minutes) ===");
        var buildCode = await ProcessRunner.RunAsync("cargo", "build --release --workspace", dir, log, ct);
        if (buildCode != 0)
        {
            log($"[!!] cargo build failed with exit code {buildCode}.");
            return false;
        }

        if (!IsInstalled(p))
        {
            log($"[!!] Build finished, but '{ExePath(p)}' was not found.");
            return false;
        }

        var head = await GetHeadAsync(dir, ct);
        config.InstalledVersions[p.Id] = head;
        log($"[ok] {p.Name} is ready ({head}).");
        return true;
    }

    private static async Task<string> GetHeadAsync(string dir, CancellationToken ct, bool full = false)
    {
        var lines = new List<string>();
        var code = await ProcessRunner.RunAsync(
            "git", full ? "rev-parse HEAD" : "rev-parse --short HEAD", dir,
            s => { lock (lines) lines.Add(s); }, ct);
        lock (lines) return code == 0 ? (lines.FirstOrDefault()?.Trim() ?? "") : "";
    }

    public static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                try { total += new FileInfo(file).Length; } catch { }
        }
        catch { }
        return total;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}

public sealed class LinuxLaunchService
{
    public async Task<Process> Launch(
        RunnerProfile p, string gameFolder, string gameExe, Action<string> log,
        bool usePipeline = true, bool seriesS = false, string? resolution = null,
        Action? onExited = null, CancellationToken ct = default)
    {
        var exe = LinuxRunnerService.ExePath(p);
        if (!File.Exists(exe)) throw new FileNotFoundException($"{p.Name} is not installed yet.", exe);

        var workDir = Path.GetDirectoryName(exe)!;
        var env = new Dictionary<string, string?>
        {
            ["VICTORY_NO_WATCHDOG"] = "1"
        };

        if (!string.IsNullOrWhiteSpace(LinuxRunnerService.CustomCacheRoot))
            env["VICTORY_CACHE_DIR"] = LinuxRunnerService.CustomCacheRoot;

        var resValue = resolution switch
        {
            "540p" => "960x540",
            "720p" => "1280x720",
            "1080p" => "1920x1080",
            "2K" => "2560x1440",
            "4K" => "3840x2160",
            _ => null
        };
        if (resValue != null) env["VICTORY_RESOLUTION"] = resValue;

        if (string.Equals(p.Id, "klbf-lab", StringComparison.OrdinalIgnoreCase))
        {
            env["VICTORY_SHADER_OPT"] = "0";
            env["VICTORY_LATE_WAIT"] = "1";
            env["VICTORY_WIDE_BACK"] = "1";
            env["VICTORY_PIPELINE_WAIT_MS"] = "24";
            env["VICTORY_RELAY_COPY_QUEUE"] = "0";
        }

        var useWine = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        if (usePipeline && p.PreLaunchArgs.Count > 0)
        {
            log("WARNING: Pipeline/shader translation is enabled. This may take several minutes...");
            foreach (var raw in p.PreLaunchArgs)
            {
                var preArgs = FormatArgs(raw, gameFolder, gameExe);
                var file = useWine ? "wine" : exe;
                var fullArgs = useWine ? $"\"{exe}\" {preArgs}" : preArgs;
                log($"> {file} {fullArgs}");
                var code = await ProcessRunner.RunAsync(file, fullArgs, workDir, log, ct, env);
                if (code != 0) throw new InvalidOperationException($"Pre-launch failed with exit code {code}.");
            }
        }

        var args = FormatArgs(p.LaunchArgs, gameFolder, gameExe);
        if (p.SupportsConsole && !p.UsesConsoleEnv)
            args += seriesS ? " --console series-s" : " --console series-x";
        if (p.UsesConsoleEnv)
            env["XCL_DEVICE_TYPE"] = seriesS ? "lockhart" : "anaconda";

        var launchFile = useWine ? "wine" : exe;
        var launchArgs = useWine ? $"\"{exe}\" {args}" : args;
        log($"> {launchFile} {launchArgs}");

        var psi = new ProcessStartInfo
        {
            FileName = launchFile,
            Arguments = launchArgs,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var kv in env)
            if (kv.Value != null) psi.Environment[kv.Key] = kv.Value;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.Exited += (_, _) =>
        {
            try { log($"Process exited with code {process.ExitCode}."); } catch { }
            onExited?.Invoke();
        };

        if (!process.Start()) throw new InvalidOperationException("Failed to start process.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static string FormatArgs(string template, string gameFolder, string gameExe) =>
        template
            .Replace("{gameFolder}", gameFolder.TrimEnd('/', '\\'))
            .Replace("{gameExe}", gameExe);
}
