// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public enum UpdateStatus { NotInstalled, UpToDate, UpdateAvailable, Unknown }

/// <summary>Installs requirements, downloads the runner's source with git and builds it.</summary>
public sealed class RunnerService
{
    private readonly DependencyService _dependencies = new();

    public static string CustomRunnersRoot { get; set; } = "";
    public static string CustomCacheRoot { get; set; } = "";

    public static string EffectiveRunnersRoot =>
        !string.IsNullOrWhiteSpace(CustomRunnersRoot) ? CustomRunnersRoot : Paths.Root;

    public static string SourceDir(RunnerProfile p) => Path.Combine(EffectiveRunnersRoot, "sources", p.Id);
    public static string ExePath(RunnerProfile p) => Path.Combine(SourceDir(p), p.Exe);
    public static string PipelineCacheDir =>
        !string.IsNullOrWhiteSpace(CustomCacheRoot)
            ? CustomCacheRoot
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "victory",
                "cache");

    public static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { /* ignore locked/inaccessible files */ }
            }
        }
        catch { /* ignore */ }
        return total;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var idx = 0;
        while (size >= 1024 && idx < units.Length - 1)
        {
            size /= 1024;
            idx++;
        }
        return $"{size:0.##} {units[idx]}";
    }

    public static void DeleteDirectorySafe(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }

    public bool IsInstalled(RunnerProfile p) => File.Exists(ExePath(p));

    /// <summary>
    /// Stops every running process that was started from this runner's own built exe
    /// (e.g. a leftover victory.exe in the background). Processes with the same name that live
    /// somewhere else, such as another runner's victory.exe, are not touched.
    /// Returns how many processes were stopped.
    /// </summary>
    public static int KillRunnerProcesses(RunnerProfile p)
    {
        var killed = 0;
        foreach (var proc in FindRunnerProcesses(p))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
                killed++;
            }
            catch { /* the process already exited */ }
            finally { proc.Dispose(); }
        }

        return killed;
    }

    /// <summary>
    /// Total CPU time used so far by the running processes of this runner's own exe,
    /// and how many such processes there are. Used to show how busy the runner is.
    /// </summary>
    public static (TimeSpan Cpu, int Count) GetRunnerCpuTime(RunnerProfile p)
    {
        var total = TimeSpan.Zero;
        var count = 0;
        foreach (var proc in FindRunnerProcesses(p))
        {
            try
            {
                total += proc.TotalProcessorTime;
                count++;
            }
            catch { /* the process exited while we were reading it */ }
            finally { proc.Dispose(); }
        }

        return (total, count);
    }

    /// <summary>Running processes that were started from this runner's own built exe. The caller disposes them.</summary>
    private static List<Process> FindRunnerProcesses(RunnerProfile p)
    {
        var exePath = Path.GetFullPath(ExePath(p));
        var found = new List<Process>();

        foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath)))
        {
            try
            {
                var path = proc.MainModule?.FileName;
                if (path != null && string.Equals(Path.GetFullPath(path), exePath, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(proc);
                    continue;
                }
            }
            catch { /* access denied, or the process already exited */ }

            proc.Dispose();
        }

        return found;
    }

    /// <summary>Short git revision of the installed source, or "" if unknown.</summary>
    public async Task<string> GetInstalledVersionAsync(RunnerProfile p, CancellationToken ct = default)
    {
        var dir = SourceDir(p);
        if (!Directory.Exists(Path.Combine(dir, ".git"))) return "";
        try { return await GetHeadAsync(dir, ct); }
        catch { return ""; }
    }

    /// <summary>Compares the installed revision with the remote default branch (needs internet).</summary>
    public async Task<UpdateStatus> CheckForUpdateAsync(RunnerProfile p, CancellationToken ct = default, bool sourceOnly = false)
    {
        // sourceOnly: runners without a built exe (Nvidia page) count as installed when the git clone exists.
        if (sourceOnly ? !HasSource(p) : !IsInstalled(p)) return UpdateStatus.NotInstalled;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));

            var local = await GetHeadAsync(SourceDir(p), cts.Token, full: true);
            if (local == "") return UpdateStatus.Unknown;

            var lines = new List<string>();
            var code = await ProcessRunner.RunAsync(
                "git", $"ls-remote \"{p.GitUrl}\" HEAD", null,
                s => { lock (lines) lines.Add(s); }, cts.Token);
            if (code != 0) return UpdateStatus.Unknown;

            string remote;
            lock (lines)
            {
                remote = lines.Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length >= 40 && l[..40].All(Uri.IsHexDigit))?[..40] ?? "";
            }
            if (remote == "") return UpdateStatus.Unknown;

            return string.Equals(local, remote, StringComparison.OrdinalIgnoreCase)
                ? UpdateStatus.UpToDate
                : UpdateStatus.UpdateAvailable;
        }
        catch
        {
            return UpdateStatus.Unknown;   // offline, git missing, timeout ...
        }
    }

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

            if (code != 0)
                return RunnerHealthState.Broken;

            if (!IsInstalled(p))
                return RunnerHealthState.NotInstalled;

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

    public static bool HasSource(RunnerProfile p) => Directory.Exists(Path.Combine(SourceDir(p), ".git"));

    /// <summary>Short git revision of the downloaded source, or "" if there is none.</summary>
    public async Task<string> GetSourceVersionAsync(RunnerProfile p, CancellationToken ct = default)
    {
        if (!HasSource(p)) return "";
        try { return await GetHeadAsync(SourceDir(p), ct); }
        catch { return ""; }
    }

    /// <summary>
    /// Downloads or updates the runner's source with git only. Nothing from the runner is built or run,
    /// so the new code can be reviewed before <see cref="BuildAsync"/> is clicked.
    /// Returns true when the source is there and current.
    /// </summary>
    public async Task<bool> UpdateSourceAsync(
        RunnerProfile p, Action<string> log, bool reviewStep = true, CancellationToken ct = default)
    {
        // With Review Before Build off this is only the first half of Install / Update: say nothing about reviewing.
        var notBuilt = reviewStep ? " Nothing was built." : "";
        // Only git is needed here; the build tools are installed by BuildAsync.
        log($"=== {p.Name}: checking git ===");
        foreach (var dependency in p.Dependencies.Where(d =>
                     string.Equals(d.Check, "command:git", StringComparison.OrdinalIgnoreCase)))
        {
            if (!await _dependencies.EnsureAsync(dependency, log, ct)) return false;
        }

        var dir = SourceDir(p);
        var before = "";
        if (HasSource(p))
        {
            before = await GetHeadAsync(dir, ct, full: true);
            log(reviewStep ? "=== Updating source (download only, nothing is built) ===" : "=== Updating source ===");
            // Fetch then reset hard so diverging/rebased upstream branches never block the update.
            // Runner source dirs are never modified by the launcher, so discarding local state is safe.
            if (await ProcessRunner.RunAsync("git", "fetch --prune origin", dir, log, ct) != 0)
            {
                log("[!!] git fetch failed.");
                return false;
            }
            // Determine the upstream tracking branch (e.g. origin/main or origin/master)
            var trackResult = await ProcessRunner.RunAndCaptureAsync("git", "rev-parse --abbrev-ref --symbolic-full-name @{u}", dir, ct);
            var upstream = string.IsNullOrWhiteSpace(trackResult) ? "origin/HEAD" : trackResult.Trim();
            if (await ProcessRunner.RunAsync("git", $"reset --hard {upstream}", dir, log, ct) != 0)
            {
                log("[!!] git reset failed.");
                return false;
            }
        }
        else
        {
            log(reviewStep ? "=== Downloading source (nothing is built) ===" : "=== Downloading source ===");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);   // leftover from a failed clone
            Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
            if (await ProcessRunner.RunAsync("git", $"clone --depth 1 \"{p.GitUrl}\" \"{dir}\"", null, log, ct) != 0)
            {
                log("[!!] git clone failed.");
                return false;
            }
        }

        var after = await GetHeadAsync(dir, ct, full: true);
        var shortAfter = after.Length > 7 ? after[..7] : after;
        if (before == "")
        {
            log($"[ok] Source downloaded ({shortAfter}).{notBuilt}");
        }
        else if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase))
        {
            log($"[ok] Source is already up to date ({shortAfter}). Nothing new was downloaded.");
            return true;
        }
        else
        {
            var shortBefore = before.Length > 7 ? before[..7] : before;
            log($"[ok] Source updated {shortBefore} -> {shortAfter}.{notBuilt} New commits:");
            var commits = await ProcessRunner.RunAndCaptureAsync(
                "git", $"log --oneline --no-decorate -n 30 {before}..{after}", dir, ct);
            foreach (var line in commits.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                log("    " + line.TrimEnd());
            var stat = await ProcessRunner.RunAndCaptureAsync("git", $"diff --shortstat {before} {after}", dir, ct);
            if (!string.IsNullOrWhiteSpace(stat)) log("   " + stat.Trim());
        }

        if (reviewStep) log($"Review the source in {dir}, then click 'Build'.");
        return true;
    }

    /// <summary>
    /// "Install / Update" (Review Before Build off): downloads or updates the source, then builds it.
    /// Returns true when the runner is built and ready to launch.
    /// </summary>
    public async Task<bool> InstallOrUpdateAsync(
        RunnerProfile p, AppConfig config, Action<string> log, CancellationToken ct = default) =>
        await UpdateSourceAsync(p, log, reviewStep: false, ct) && await BuildAsync(p, config, log, ct);

    /// <summary>
    /// Builds the source that is already on disk. The runner's source is not updated here
    /// (use <see cref="UpdateSourceAsync"/>), so only code that could be reviewed beforehand is built.
    /// Returns true when the runner is built and ready to launch.
    /// </summary>
    public async Task<bool> BuildAsync(
        RunnerProfile p, AppConfig config, Action<string> log, CancellationToken ct = default)
    {
        if (!HasSource(p))
        {
            log($"[!!] {p.Name} has no source yet. Click 'Update Source' first.");
            return false;
        }

        // 1) Requirements (git, build tools, rust ...)
        log($"=== {p.Name}: checking requirements ===");
        foreach (var dependency in p.Dependencies)
        {
            if (!await _dependencies.EnsureAsync(dependency, log, ct)) return false;
        }
        // Rust needs the MSVC linker AND the Windows SDK libraries; Build Tools installed by hand often lack the SDK.
        if (p.Dependencies.Any(d => d.Check == "vsbuildtools") && !await DependencyService.EnsureWindowsSdkAsync(log, ct))
            return false;

        var dir = SourceDir(p);
        var after = await GetHeadAsync(dir, ct);
        // Windows 10: a runner built before the launcher added the Agility SDK exports is built
        // again, though its source did not change (AgilityService).
        var missingWindows10Support = AgilityService.Needed && IsInstalled(p) && !AgilityService.HasExports(ExePath(p));
        if (IsInstalled(p) && !missingWindows10Support &&
            config.InstalledVersions.TryGetValue(p.Id, out var built) &&
            string.Equals(built, after, StringComparison.OrdinalIgnoreCase))
        {
            log($"Already built from this source ({after}).");
            // (A failed download is said in the log; the launch tries again.)
            await AgilityService.EnsureRuntimeAsync(ExePath(p), log, ct);
            return true;
        }
        if (missingWindows10Support)
            log("Windows 10: this runner was built without the D3D12 Agility SDK; building it again.");

        // 2) Build
        log($"=== Building {after} from the local source (this can take several minutes) ===");
        var buildEnv = await AgilityService.BuildEnvironmentAsync(log, dir, ct);
        var vcvars = await DependencyService.FindVcvars64Async(ct) ?? FindVcvars64();
        if (vcvars == null) log("[!!] vcvars64.bat was not found; the build may fail to find link.exe.");
        var buildScript = Path.Combine(Path.GetTempPath(), "AmericasLauncher-build.cmd");
        foreach (var command in p.BuildCommands)
        {
            log($"> {command}");
            // Run through a script file instead of one long "cmd /c" line: no quoting surprises with "(x86)" paths,
            // vcvars' own noise ("The system cannot find the file specified.") is hidden, and a missing LIB is reported.
            var lines = new List<string> { "@echo off", "chcp 65001 >NUL" };
            if (vcvars != null)
            {
                lines.Add("set \"VSCMD_SKIP_SENDTELEMETRY=1\"");
                lines.Add($"call \"{vcvars}\" >NUL 2>&1");
                lines.Add("if not defined LIB echo [!!] vcvars did not set LIB: the Windows SDK or C++ tools are not installed correctly.");
            }
            lines.Add(command);
            lines.Add("exit /b %ERRORLEVEL%");
            await File.WriteAllLinesAsync(buildScript, lines, new System.Text.UTF8Encoding(false), ct);

            var duplicateSymbols = false;
            void BuildLog(string line)
            {
                if (line.Contains("LNK2005", StringComparison.Ordinal) || line.Contains("LNK1169", StringComparison.Ordinal))
                    duplicateSymbols = true;
                log(line);
            }

            var code = await ProcessRunner.RunAsync("cmd.exe", $"/d /c \"\"{buildScript}\"\"", dir, BuildLog, ct, buildEnv);
            if (code != 0 && duplicateSymbols && buildEnv != null)
            {
                // The runner already defines the Agility SDK symbols (its source was not recognised): link without
                // the launcher's exports object and use the runner's own runtime version.
                log("[!!] Duplicate D3D12SDKVersion / D3D12SDKPath symbols: the runner exports them itself. Building again without the launcher's exports.");
                buildEnv = null;
                code = await ProcessRunner.RunAsync("cmd.exe", $"/d /c \"\"{buildScript}\"\"", dir, log, ct, null);
            }
            if (code != 0)
            {
                log($"[!!] Command failed with exit code {code}.");
                if (code == 1181 || !DependencyService.HasWindowsSdk())
                    log("[!!] LNK1181 means the linker cannot find a system library. Install \"Windows 11 SDK\" (or Windows 10 SDK) " +
                        "with the Visual Studio Installer (Modify on Build Tools), then run Install / Update (or Build) again.");
                return false;
            }
        }

        if (!IsInstalled(p))
        {
            log($"[!!] Build finished, but '{p.Exe}' was not found.");
            return false;
        }

        if (AgilityService.Needed)
        {
            if (AgilityService.HasExports(ExePath(p)))
                await AgilityService.EnsureRuntimeAsync(ExePath(p), log, ct);
            else
                log("[!!] Windows 10: the built runner has no D3D12 Agility SDK exports; it may fail with " +
                    "\"the device supports SM 0x65 only\" (not a Rust/cargo runner?).");
        }

        config.InstalledVersions[p.Id] = after;
        log($"[ok] {p.Name} is ready ({after}).");
        return true;
    }

    private static async Task<string> GetHeadAsync(string dir, CancellationToken ct, bool full = false)
    {
        var lines = new List<string>();
        var code = await ProcessRunner.RunAsync(
            "git", full ? "rev-parse HEAD" : "rev-parse --short HEAD", dir, s => { lock (lines) lines.Add(s); }, ct);

        lock (lines) return code == 0 ? (lines.FirstOrDefault()?.Trim() ?? "") : "";
    }

    private static string? FindVcvars64()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] roots = [pf86, pf];
        string[] years = ["2022", "2019", "18"];
        string[] editions = ["BuildTools", "Community", "Professional", "Enterprise"];

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            foreach (var year in years)
            {
                foreach (var ed in editions)
                {
                    var candidate = Path.Combine(root, "Microsoft Visual Studio", year, ed, "VC", "Auxiliary", "Build", "vcvars64.bat");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }
}
