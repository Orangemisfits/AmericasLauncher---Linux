// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public enum UpdateStatus { NotInstalled, UpToDate, UpdateAvailable, Unknown }

/// <summary>
/// Installs requirements, downloads the runner's source with git and cross-compiles it for Windows
/// (x86_64-pc-windows-msvc, with cargo-xwin). The result runs through Wine or Proton.
/// </summary>
public sealed class RunnerService
{
    private readonly DependencyService _dependencies = new();
    private readonly WineRuntimeService _runtime = new();

    public static string SourceDir(RunnerProfile p) => Path.Combine(Paths.Sources, p.Id);

    public static string? ExePath(RunnerProfile p) =>
        p.Linux is { Exe.Length: > 0 } l ? Path.Combine(SourceDir(p), l.Exe.Replace('\\', '/')) : null;

    public bool IsInstalled(RunnerProfile p) => ExePath(p) is { } exe && File.Exists(exe);

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

            var (code, lines) = await ProcessRunner.CaptureAsync("git", ["ls-remote", p.GitUrl, "HEAD"], null, cts.Token);
            if (code != 0) return UpdateStatus.Unknown;

            var remote = lines.FirstOrDefault(l => l.Length >= 40 && l[..40].All(Uri.IsHexDigit))?[..40] ?? "";
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

    /// <summary>Returns true when the runner is built and ready to launch.</summary>
    public async Task<bool> InstallOrUpdateAsync(
        RunnerProfile p, AppConfig config, Action<string> log, CancellationToken ct = default)
    {
        if (p.Linux is not { } linux)
        {
            log($"[!!] {p.Name} has no Linux build recipe yet (\"linux\" entry in runners.json).");
            return false;
        }

        // 1) Requirements (git, compilers, rust, cargo-xwin)
        log($"=== {p.Name}: checking requirements ===");
        if (!await _dependencies.EnsureAllAsync(linux.Dependencies, log, ct)) return false;

        // The runtime is checked before the long build, so that a missing Wine shows up at once.
        var (runtime, problem) = WineRuntimeService.ResolveRuntime(config);
        if (runtime == null)
        {
            log("[!!] " + problem);
            return false;
        }

        // 2) Source code
        var dir = SourceDir(p);
        var before = "";
        if (Directory.Exists(Path.Combine(dir, ".git")))
        {
            before = await GetHeadAsync(dir, ct);
            log("=== Updating source ===");
            if (await ProcessRunner.RunAsync("git", ["pull", "--ff-only"], dir, log, ct) != 0)
            {
                log("[!!] git pull failed.");
                return false;
            }
        }
        else
        {
            log("=== Downloading source ===");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);   // leftover from a failed clone
            Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
            if (await ProcessRunner.RunAsync("git", ["clone", "--depth", "1", p.GitUrl, dir], null, log, ct) != 0)
            {
                log("[!!] git clone failed.");
                return false;
            }
        }

        var after = await GetHeadAsync(dir, ct);
        if (before != "" && before == after && IsInstalled(p))
        {
            log($"Already up to date ({after}).");
        }
        else
        {
            // 3) Build
            log("=== Building for Windows with cargo-xwin (this can take several minutes) ===");
            log("NOTE: the first build downloads the Microsoft C runtime and Windows SDK libraries (several hundred MB) into ~/.cache/cargo-xwin.");
            var env = new Dictionary<string, string> { ["CARGO_TERM_COLOR"] = "never" };
            foreach (var command in linux.BuildCommands)
            {
                log($"> {command}");
                var code = await ProcessRunner.RunShellAsync(command, dir, log, ct, env);
                if (code != 0)
                {
                    log($"[!!] Command failed with exit code {code}.");
                    return false;
                }
            }

            if (!IsInstalled(p))
            {
                log($"[!!] Build finished, but '{linux.Exe}' was not found.");
                return false;
            }
        }

        config.InstalledVersions[p.Id] = after;

        // 4) Runtime pieces: Wine prefix + vkd3d-proton, DXC next to the runner if it needs it
        if (!await _runtime.PrepareAsync(config, log, ct))
        {
            log("[!!] The runner is built, but the Windows runtime is not ready (see above).");
            return false;
        }
        if (linux.NeedsDxc && !await _runtime.EnsureDxcAsync(Path.GetDirectoryName(ExePath(p)!)!, log, ct))
            return false;

        log($"[ok] {p.Name} is ready ({after}).");
        return true;
    }

    private static async Task<string> GetHeadAsync(string dir, CancellationToken ct, bool full = false)
    {
        var args = full ? new[] { "rev-parse", "HEAD" } : new[] { "rev-parse", "--short", "HEAD" };
        var (code, lines) = await ProcessRunner.CaptureAsync("git", args, dir, ct);
        return code == 0 ? (lines.FirstOrDefault() ?? "") : "";
    }
}
