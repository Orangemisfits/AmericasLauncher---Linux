// SPDX-License-Identifier: GPL-2.0-only
using System.ComponentModel;
using System.IO;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public sealed class DependencyService
{
    /// <summary>
    /// Programs installed by winget change PATH in the registry, but not in this running process.
    /// Reload it so newly installed tools (git, cargo) can be found without restarting.
    /// </summary>
    public static void RefreshPath()
    {
        var machine = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        var combined = Environment.ExpandEnvironmentVariables(machine + ";" + user);
        Environment.SetEnvironmentVariable("Path", combined, EnvironmentVariableTarget.Process);
    }

    public async Task<bool> IsInstalledAsync(Dependency d, CancellationToken ct = default)
    {
        if (d.Check.StartsWith("command:", StringComparison.Ordinal))
        {
            var command = d.Check["command:".Length..];
            var code = await ProcessRunner.RunAsync("cmd.exe", $"/c where {command}", null, _ => { }, ct);
            return code == 0;
        }

        if (d.Check == "vsbuildtools")
            return await HasVcToolsAsync(ct);

        return false;
    }

    private static async Task<bool> HasVcToolsAsync(CancellationToken ct)
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere)) return false;

        var lines = new List<string>();
        await ProcessRunner.RunAsync(
            vswhere,
            "-products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -latest -property installationPath",
            null,
            s => { lock (lines) lines.Add(s); },
            ct);

        lock (lines) return lines.Any(l => !string.IsNullOrWhiteSpace(l));
    }

    /// <summary>Installs the dependency with winget if it is missing. Returns true if it is available afterwards.</summary>
    public async Task<bool> EnsureAsync(Dependency d, Action<string> log, CancellationToken ct = default)
    {
        if (await IsInstalledAsync(d, ct))
        {
            log($"[ok] {d.Name} is already installed.");
            return true;
        }

        log($"[..] Installing {d.Name} with winget. A Windows permission prompt may appear and this can take a while.");

        var args = $"install --id {d.WingetId} --exact --accept-package-agreements --accept-source-agreements";
        args += string.IsNullOrWhiteSpace(d.WingetOverride)
            ? " --silent"
            : $" --override \"{d.WingetOverride}\"";

        try
        {
            await ProcessRunner.RunAsync("winget", args, null, Filter(log), ct);
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException(
                "winget was not found. Install or update 'App Installer' from the Microsoft Store, then try again.");
        }

        RefreshPath();

        if (await IsInstalledAsync(d, ct))
        {
            log($"[ok] {d.Name} installed.");
            return true;
        }

        log($"[!!] {d.Name} could not be verified after installation. " +
            "If the installer finished, close and reopen the launcher, then try again.");
        return false;
    }

    // winget prints spinners and progress bars; keep only lines with readable text.
    private static Action<string> Filter(Action<string> log) => line =>
    {
        var text = line.Trim();
        if (text.Length > 1 && text.Any(char.IsLetterOrDigit)) log(text);
    };
}
