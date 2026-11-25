// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public sealed class LaunchService
{
    public async Task<Process> Launch(RunnerProfile p, string gameFolder, string gameExe, Action<string> log)
    {
        var exe = RunnerService.ExePath(p);
        if (!File.Exists(exe)) throw new FileNotFoundException($"{p.Name} is not installed yet.", exe);
        if (!Directory.Exists(gameFolder)) throw new DirectoryNotFoundException("Game folder not found: " + gameFolder);
        if (!File.Exists(gameExe)) throw new FileNotFoundException("Game exe not found.", gameExe);

        var workDir = Path.GetDirectoryName(exe)!;

        // 1) Ön hazırlık komutları varsa (ör. translate) sırayla çalıştır ve bitmesini bekle
        foreach (var rawArgs in p.PreLaunchArgs)
        {
            var preArgs = FormatArgs(rawArgs, gameFolder, gameExe);
            log($"> {preArgs}");
            var code = await ProcessRunner.RunAsync(exe, preArgs, workDir, log);
            if (code != 0)
                throw new InvalidOperationException($"Pre-launch command failed with exit code {code}.");
        }

        // 2) Ön hazırlık bittikten sonra asıl oyunu başlat
        var args = FormatArgs(p.LaunchArgs, gameFolder, gameExe);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false
        };
        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the process.");
    }

    private static string FormatArgs(string template, string gameFolder, string gameExe) =>
        template
            .Replace("{gameFolder}", gameFolder.TrimEnd('\\', '/'))
            .Replace("{gameExe}", gameExe);
}
