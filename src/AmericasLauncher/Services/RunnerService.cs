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
    public async Task<UpdateStatus> CheckForUpdateAsync(RunnerProfile p, CancellationToken ct = default)
    {
        if (!IsInstalled(p)) return UpdateStatus.NotInstalled;
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

    /// <summary>Returns true when the runner is built and ready to launch.</summary>
    public async Task<bool> InstallOrUpdateAsync(
        RunnerProfile p, AppConfig config, Action<string> log, CancellationToken ct = default)
    {
        // 1) Requirements (git, build tools, rust ...)
        log($"=== {p.Name}: checking requirements ===");
        foreach (var dependency in p.Dependencies)
        {
            if (!await _dependencies.EnsureAsync(dependency, log, ct)) return false;
        }

        // 2) Source code
        var dir = SourceDir(p);
        var before = "";
        if (Directory.Exists(Path.Combine(dir, ".git")))
        {
            before = await GetHeadAsync(dir, ct);
            log("=== Updating source ===");
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
            log("=== Downloading source ===");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);   // leftover from a failed clone
            Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
            if (await ProcessRunner.RunAsync("git", $"clone --depth 1 \"{p.GitUrl}\" \"{dir}\"", null, log, ct) != 0)
            {
                log("[!!] git clone failed.");
                return false;
            }
        }

        var after = await GetHeadAsync(dir, ct);
        // Windows 10: a runner built before the launcher added the Agility SDK exports is built
        // again, though its source did not change (AgilityService).
        var missingWindows10Support = AgilityService.Needed && IsInstalled(p) && !AgilityService.HasExports(ExePath(p));
        if (before != "" && before == after && IsInstalled(p) && !missingWindows10Support)
        {
            log($"Already up to date ({after}).");
            config.InstalledVersions[p.Id] = after;
            // (A failed download is said in the log; the launch tries again.)
            await AgilityService.EnsureRuntimeAsync(ExePath(p), log, ct);
            return true;
        }
        if (missingWindows10Support)
            log("Windows 10: this runner was built without the D3D12 Agility SDK; building it again.");

        // 3) Build
        log("=== Building (this can take several minutes) ===");
        var buildEnv = await AgilityService.BuildEnvironmentAsync(log, ct);
        var vcvars = FindVcvars64();
        foreach (var command in p.BuildCommands)
        {
            log($"> {command}");
            var fullCmd = vcvars != null ? $"call \"{vcvars}\" >NUL && {command}" : command;
            var code = await ProcessRunner.RunAsync("cmd.exe", "/c " + fullCmd, dir, log, ct, buildEnv);
            if (code != 0)
            {
                log($"[!!] Command failed with exit code {code}.");
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
