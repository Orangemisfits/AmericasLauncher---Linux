// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

/// <summary>Installs requirements, downloads the runner's source with git and builds it.</summary>
public sealed class RunnerService
{
    private readonly DependencyService _dependencies = new();

    public static string SourceDir(RunnerProfile p) => Path.Combine(Paths.Root, "sources", p.Id);
    public static string ExePath(RunnerProfile p) => Path.Combine(SourceDir(p), p.Exe);

    public bool IsInstalled(RunnerProfile p) => File.Exists(ExePath(p));

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
            if (await ProcessRunner.RunAsync("git", "pull --ff-only", dir, log, ct) != 0)
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
            if (await ProcessRunner.RunAsync("git", $"clone --depth 1 \"{p.GitUrl}\" \"{dir}\"", null, log, ct) != 0)
            {
                log("[!!] git clone failed.");
                return false;
            }
        }

        var after = await GetHeadAsync(dir, ct);
        if (before != "" && before == after && IsInstalled(p))
        {
            log($"Already up to date ({after}).");
            config.InstalledVersions[p.Id] = after;
            return true;
        }

        // 3) Build
        log("=== Building (this can take several minutes) ===");
        foreach (var command in p.BuildCommands)
        {
            log($"> {command}");
            var code = await ProcessRunner.RunAsync("cmd.exe", "/c " + command, dir, log, ct);
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

        config.InstalledVersions[p.Id] = after;
        log($"[ok] {p.Name} is ready ({after}).");
        return true;
    }

    private static async Task<string> GetHeadAsync(string dir, CancellationToken ct)
    {
        var lines = new List<string>();
        var code = await ProcessRunner.RunAsync(
            "git", "rev-parse --short HEAD", dir, s => { lock (lines) lines.Add(s); }, ct);

        lock (lines) return code == 0 ? (lines.FirstOrDefault()?.Trim() ?? "") : "";
    }
}
