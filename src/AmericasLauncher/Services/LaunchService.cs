// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public sealed class LaunchService
{
    /// <summary>
    /// Runs the optional pre-launch commands (pipeline / shader translation) and then starts the game.
    /// Cancelling <paramref name="ct"/> stops a running pre-launch command (and its child processes)
    /// and throws <see cref="OperationCanceledException"/>.
    /// <paramref name="onExited"/> is called (possibly from a background thread) when the game process exits.
    /// </summary>
    public async Task<Process> Launch(
        RunnerProfile p, string gameFolder, string gameExe, Action<string> log,
        bool usePipeline = true, Action? onExited = null, CancellationToken ct = default,
        RunnerLaunchOptions? options = null)
    {
        var exe = RunnerService.ExePath(p);
        if (!File.Exists(exe)) throw new FileNotFoundException($"{p.Name} is not installed yet.", exe);
        if (!Directory.Exists(gameFolder)) throw new DirectoryNotFoundException("Game folder not found: " + gameFolder);
        if (!File.Exists(gameExe)) throw new FileNotFoundException("Game exe not found.", gameExe);

        var workDir = Path.GetDirectoryName(exe)!;

        // The user's launch settings for this runner (Settings > Launch Options), for every process below.
        var env = LaunchOptionsService.ResolveEnv(options, RunnerService.SourceDir(p), gameFolder, gameExe, log);
        if (options is { IsEmpty: false })
        {
            log($"Launch options for {p.Name}: {env.Count} environment variable(s)" +
                (string.IsNullOrWhiteSpace(options.ExtraArgs) ? "" : $", extra arguments: {options.ExtraArgs.Trim()}"));
            if (env.Count > 0) log("  " + string.Join(", ", env.Keys));
        }

        // 1) Run the pre-launch commands (e.g. translate) one by one and wait for each to finish.
        if (usePipeline && p.PreLaunchArgs.Count > 0)
        {
            log("WARNING: Pipeline/shader translation is enabled. This will take a long time (several minutes on the first run) — please wait...");
            foreach (var rawArgs in p.PreLaunchArgs)
            {
                ct.ThrowIfCancellationRequested();
                var preArgs = FormatArgs(rawArgs, gameFolder, gameExe);
                log($"> {preArgs}");
                var code = await ProcessRunner.RunAsync(exe, preArgs, workDir, log, ct, env);
                if (code != 0)
                    throw new InvalidOperationException($"Pre-launch command failed with exit code {code}.");
            }
        }

        // 2) Start the game itself and stream its output into the launcher's log box.
        ct.ThrowIfCancellationRequested();
        var args = FormatArgs(LaunchOptionsService.ComposeArgs(p.LaunchArgs, options), gameFolder, gameExe);
        log($"> {args}");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        LaunchOptionsService.Apply(psi.Environment, env);

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.Exited += (_, _) =>
        {
            try { log($"Process exited with code {process.ExitCode}."); }
            catch { /* ignore if window already closed */ }

            try { onExited?.Invoke(); }
            catch { /* ignore if window already closed */ }
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Failed to start the process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static string FormatArgs(string template, string gameFolder, string gameExe) =>
        template
            .Replace("{gameFolder}", gameFolder.TrimEnd('\\', '/'))
            .Replace("{gameExe}", gameExe);
}
