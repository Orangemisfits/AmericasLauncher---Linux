// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public sealed class LaunchService
{
    public Process Launch(EmulatorProfile p, string gamePath)
    {
        var dir = Paths.EmulatorDir(p.Id);
        var exe = Path.Combine(dir, p.Exe);
        if (!File.Exists(exe)) throw new FileNotFoundException("Emulator is not installed.", exe);
        if (!File.Exists(gamePath)) throw new FileNotFoundException("Game file not found.", gamePath);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = p.LaunchArgs.Replace("{gamePath}", gamePath),
            WorkingDirectory = dir,      // emulator folder, for portable settings
            UseShellExecute = false
        };
        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the process.");
    }
}
