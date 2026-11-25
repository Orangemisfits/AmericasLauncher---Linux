// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public sealed class LaunchService
{
    private readonly WineRuntimeService _runtime = new();

    /// <summary>
    /// Runs the runner's pre-launch commands (shader translation ...) and then the game, all through
    /// Wine or Proton. Game paths are handed to the runner as Windows paths on drive Z:.
    /// </summary>
    public async Task<Process> Launch(RunnerProfile p, AppConfig config, Action<string> log, CancellationToken ct = default)
    {
        var exe = RunnerService.ExePath(p);
        if (exe == null || !File.Exists(exe)) throw new FileNotFoundException($"{p.Name} is not installed yet.", exe);
        if (!Directory.Exists(config.GameFolder)) throw new DirectoryNotFoundException("Game folder not found: " + config.GameFolder);
        if (!File.Exists(config.GameExe)) throw new FileNotFoundException("Game exe not found.", config.GameExe);

        var (runtime, problem) = WineRuntimeService.ResolveRuntime(config);
        if (runtime == null) throw new InvalidOperationException(problem);

        if (!WineRuntimeService.IsPrefixReady(config) && !await _runtime.PrepareAsync(config, log, ct))
            throw new InvalidOperationException("The Wine prefix could not be prepared (see above).");

        var workDir = Path.GetDirectoryName(exe)!;
        if (p.Linux?.NeedsDxc == true && !await _runtime.EnsureDxcAsync(workDir, log, ct))
            throw new InvalidOperationException("The DirectX Shader Compiler is missing (see above).");

        var env = WineRuntimeService.RuntimeEnvironment(config);
        var gameFolder = LinuxEnvironment.ToWindowsPath(config.GameFolder).TrimEnd('\\');
        var gameExe = LinuxEnvironment.ToWindowsPath(config.GameExe);
        var runtimeName = Path.GetFileName(runtime);

        // 1) Pre-launch commands (e.g. translate), one after the other
        if (p.PreLaunchArgs.Count > 0)
            log("NOTE: Pipeline/shader translation is starting. This may take a long time (several minutes on the first run) — please wait...");

        foreach (var raw in p.PreLaunchArgs)
        {
            var (file, args) = WineRuntimeService.Command(runtime, exe, FormatArgs(raw, gameFolder, gameExe));
            log($"> {runtimeName} {Path.GetFileName(exe)} {Display(args.Skip(1))}");
            var code = await ProcessRunner.RunAsync(file, args, workDir, log, ct, env);
            if (code != 0)
                throw new InvalidOperationException($"Pre-launch command failed with exit code {code}.");
        }

        // 2) The game itself, its output streamed into the launcher's log
        var (fileName, arguments) = WineRuntimeService.Command(runtime, exe, FormatArgs(p.LaunchArgs, gameFolder, gameExe));
        log($"> {runtimeName} {Path.GetFileName(exe)} {Display(arguments.Skip(1))}");

        var process = ProcessRunner.Start(fileName, arguments, workDir, log, env);
        process.Exited += (_, _) =>
        {
            try { log($"Process exited with code {process.ExitCode}."); }
            catch { /* ignore if window already closed */ }
        };
        return process;
    }

    /// <summary>Ends the runner and every Windows process in the prefix.</summary>
    public static async Task StopAsync(Process? process, AppConfig config, Action<string> log)
    {
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        catch { /* already gone */ }

        if (config.Runtime != WineRuntimeKind.Wine) return;
        var (runtime, _) = WineRuntimeService.ResolveRuntime(config);
        if (runtime == null) return;

        // wineserver lives next to the wine binary in system packages and custom builds alike.
        var wineserver = Path.Combine(Path.GetDirectoryName(runtime)!, "wineserver");
        if (!File.Exists(wineserver)) wineserver = LinuxEnvironment.Which("wineserver") ?? "";
        if (wineserver == "") return;

        await ProcessRunner.RunAsync(wineserver, ["-k"], null, log, environment: WineRuntimeService.RuntimeEnvironment(config));
        log("Stopped the Wine processes of the prefix.");
    }

    private static List<string> FormatArgs(string template, string gameFolder, string gameExe) =>
        ProcessRunner.SplitArguments(template)
            .Select(a => a.Replace("{gameFolder}", gameFolder).Replace("{gameExe}", gameExe))
            .ToList();

    private static string Display(IEnumerable<string> args) =>
        string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
