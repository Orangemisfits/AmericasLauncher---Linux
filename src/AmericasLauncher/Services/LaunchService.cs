// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public sealed class LaunchService
{
    /// <summary>
    /// Starts the game with the runner's recommended settings for <paramref name="gpu"/> ("amd" or "nvidia"), under the
    /// user's own launch options (which win over the recommended ones).
    /// <paramref name="onExited"/> is called (possibly from a background thread) when the game process exits.
    /// </summary>
    public async Task<Process> Launch(
        RunnerProfile p, string gameFolder, string gameExe, Action<string> log,
        Action? onExited = null, CancellationToken ct = default,
        RunnerLaunchOptions? options = null, string consoleMode = "x", string gpu = "amd", bool useRecommended = true)
    {
        var exe = RunnerService.ExePath(p);
        if (!File.Exists(exe)) throw new FileNotFoundException($"{p.Name} is not installed yet.", exe);
        if (!Directory.Exists(gameFolder)) throw new DirectoryNotFoundException("Game folder not found: " + gameFolder);
        if (!File.Exists(gameExe)) throw new FileNotFoundException("Game exe not found.", gameExe);

        var workDir = Path.GetDirectoryName(exe)!;

        // Windows 10: the runner needs the D3D12 Agility SDK (AgilityService).
        if (AgilityService.Needed)
        {
            if (AgilityService.HasExports(exe))
                await AgilityService.EnsureRuntimeAsync(exe, log, ct);
            else
                log("WARNING: Windows 10 and this runner was built without the D3D12 Agility SDK: it will likely fail " +
                    "with \"the device supports SM 0x65 only\". Press Install / Update (or Build) to build it again.");
        }

        // The runner's recommended settings for the chosen graphics card, with the user's own launch options on top.
        // Disabled when GPU is "off" or for Nvidia - VIctory Based (manages its own settings).
        var applyRecommended = useRecommended && gpu != "off" && !string.Equals(p.Id, "nvidia", StringComparison.OrdinalIgnoreCase);
        var preset = applyRecommended ? p.PresetFor(gpu) : null;
        var userOptions = options;
        var merged = new RunnerLaunchOptions();
        if (preset != null)
        {
            foreach (var e in preset.Env) merged.Env[e.Name] = e.Value;
            merged.ExtraArgs = preset.Args ?? "";
        }
        if (options != null)
        {
            foreach (var (name, value) in options.Env) merged.Env[name] = value;
            if (!string.IsNullOrWhiteSpace(options.ExtraArgs))
                merged.ExtraArgs = (merged.ExtraArgs + " " + options.ExtraArgs).Trim();
        }
        options = merged;

        var env = LaunchOptionsService.ResolveEnv(options, RunnerService.SourceDir(p), gameFolder, gameExe, log);
        if (!env.ContainsKey("VICTORY_NO_WATCHDOG"))
            env["VICTORY_NO_WATCHDOG"] = "1";
        if (!string.IsNullOrWhiteSpace(RunnerService.CustomCacheRoot) && !env.ContainsKey("VICTORY_CACHE_DIR"))
            env["VICTORY_CACHE_DIR"] = RunnerService.CustomCacheRoot;
        if (preset != null)
            log($"Recommended settings: {p.Name} / {(gpu == "nvidia" ? "Nvidia" : "AMD")} ({preset.Env.Count} variable(s)" +
                (string.IsNullOrWhiteSpace(preset.Args) ? "" : $", arguments: {preset.Args}") + ")");
        else if (!useRecommended)
            log("Recommended settings are turned off (Settings).");
        if (userOptions is { IsEmpty: false })
            log($"Your launch options for {p.Name}: {userOptions.Env.Count} environment variable(s)" +
                (string.IsNullOrWhiteSpace(userOptions.ExtraArgs) ? "" : $", extra arguments: {userOptions.ExtraArgs.Trim()}"));

        // Start the game itself.
        // The game's output goes straight into a log file (cmd redirects it), NOT through a pipe owned by the launcher.
        // With a pipe, a slow reader (the launcher's UI) fills the small pipe buffer and the game's logging threads
        // block inside victory.exe. A file never blocks the game, and the game also survives the launcher closing.
        ct.ThrowIfCancellationRequested();
        var args = FormatArgs(LaunchOptionsService.ComposeArgs(p.LaunchArgs, options), gameFolder, gameExe);
        var envConsole = (p.UsesConsoleEnv && consoleMode != "off")
            ? (consoleMode == "s" ? "lockhart" : "anaconda")
            : null;
        if (p.SupportsConsole && !p.UsesConsoleEnv && consoleMode != "off")
            args += consoleMode == "s" ? " --console series-s" : " --console series-x";
        if (p.SupportsConsole)
            log($"Console: {(consoleMode == "off" ? "OFF" : consoleMode == "s" ? "Xbox Series S" : "Xbox Series X")}");
        log($"> {args}");

        Directory.CreateDirectory(Path.GetDirectoryName(GameLogPath)!);
        try { File.WriteAllText(GameLogPath, ""); } catch { /* the old log is still locked: append to it */ }

        var psi = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/d /c \"\"%AL_EXE%\" %AL_ARGS% >>\"%AL_LOG%\" 2>&1\"",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        LaunchOptionsService.Apply(psi.Environment, env);
        psi.Environment["AL_EXE"] = exe;
        psi.Environment["AL_ARGS"] = args;
        psi.Environment["AL_LOG"] = GameLogPath;
        if (envConsole != null) psi.Environment["XCL_DEVICE_TYPE"] = envConsole;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tailDone = new CancellationTokenSource();
        Task? tail = null;
        process.Exited += (_, _) =>
        {
            try
            {
                tailDone.Cancel();
                tail?.Wait(2000);
                log($"Process exited with code {process.ExitCode}.");
            }
            catch { /* ignore if window already closed */ }

            try { onExited?.Invoke(); }
            catch { /* ignore if window already closed */ }
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Failed to start the process.");
        }

        tail = Task.Run(() => TailAsync(GameLogPath, log, tailDone.Token));
        return process;
    }

    public static string GameLogPath => Path.Combine(Paths.Root, "logs", "game.log");

    /// <summary>Follows the game's log file and passes new lines to <paramref name="log"/>. Drains what is left when cancelled.</summary>
    private static async Task TailAsync(string path, Action<string> log, CancellationToken stop)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line != null) { log(line); continue; }
                if (stop.IsCancellationRequested) break;
                try { await Task.Delay(250, stop); } catch (OperationCanceledException) { }
            }
        }
        catch { /* the log is only a convenience; never affect the game */ }
    }

    private static string FormatArgs(string template, string gameFolder, string gameExe) =>
        template
            .Replace("{gameFolder}", gameFolder.TrimEnd('\\', '/'))
            .Replace("{gameExe}", gameExe);
}
