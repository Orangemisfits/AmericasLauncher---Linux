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

    private static string VsInstallerDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer");

    private static async Task<List<string>> VswhereAsync(string args, CancellationToken ct)
    {
        var vswhere = Path.Combine(VsInstallerDir, "vswhere.exe");
        var lines = new List<string>();
        if (!File.Exists(vswhere)) return lines;
        await ProcessRunner.RunAsync(vswhere, args, null, s => { lock (lines) lines.Add(s.Trim()); }, ct);
        lock (lines) return lines.Where(l => l.Length > 0).ToList();
    }

    /// <summary>True when the MSVC C++ compiler tools are present, however they were installed.</summary>
    private static async Task<bool> HasVcToolsAsync(CancellationToken ct)
    {
        // 1) The official component check.
        if ((await VswhereAsync(
                "-products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath", ct)).Count > 0)
            return true;

        // 2) Look for the linker itself, in case the component id differs or the install is unusual.
        if ((await VswhereAsync("-products * -find VC\\Tools\\MSVC\\**\\bin\\Hostx64\\x64\\link.exe", ct)).Count > 0)
            return true;

        // 3) Last resort: scan the usual folders directly (vswhere may be missing or outdated).
        foreach (var pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                 })
        {
            try
            {
                var root = Path.Combine(pf, "Microsoft Visual Studio");
                if (!Directory.Exists(root)) continue;
                foreach (var edition in Directory.EnumerateDirectories(root))
                foreach (var sku in Directory.EnumerateDirectories(edition))
                {
                    var msvc = Path.Combine(sku, "VC", "Tools", "MSVC");
                    if (Directory.Exists(msvc) &&
                        Directory.EnumerateFiles(msvc, "link.exe", SearchOption.AllDirectories).Any())
                        return true;
                }
            }
            catch { /* ignore inaccessible folders */ }
        }

        return false;
    }

    /// <summary>
    /// A Visual Studio / Build Tools install exists but has no C++ tools (for example it was installed without the
    /// "Desktop development with C++" workload). winget then says "already installed" and changes nothing,
    /// so add the workload with the Visual Studio Installer instead. Shows the normal Windows permission prompt.
    /// </summary>
    private static Task<bool> AddVcWorkloadAsync(Action<string> log, CancellationToken ct) =>
        ModifyVsAsync("--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended",
            "Visual Studio is installed, but without the C++ tools. Adding the \"Desktop development with C++\" workload",
            log, ct);

    /// <summary>Runs "Visual Studio Installer modify" with the Windows permission prompt. True when the installer ran.</summary>
    private static async Task<bool> ModifyVsAsync(string addArgs, string what, Action<string> log, CancellationToken ct)
    {
        var paths = await VswhereAsync("-products * -prerelease -latest -property installationPath", ct);
        var setup = Path.Combine(VsInstallerDir, "setup.exe");
        if (paths.Count == 0 || !File.Exists(setup)) return false;

        log($"[..] {what} — accept the Windows permission prompt and wait...");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = setup,
                Arguments = $"modify --installPath \"{paths[0]}\" {addArgs} --passive --norestart --wait",
                UseShellExecute = true,
                Verb = "runas"
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync(ct);
            log($"[..] Visual Studio Installer finished (exit code {proc.ExitCode}).");
            return true;
        }
        catch (Win32Exception)
        {
            log("[!!] The permission prompt was cancelled.");
            return false;
        }
    }

    /// <summary>
    /// True when a Windows 10/11 SDK with the x64 libraries (kernel32.lib) is installed. Without it the Rust
    /// linker fails with "link.exe failed: exit code: 1181" (LNK1181: cannot open input file 'kernel32.lib').
    /// </summary>
    public static bool HasWindowsSdk()
    {
        try
        {
            var kits = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Lib");
            return Directory.Exists(kits) && Directory.EnumerateDirectories(kits)
                .Any(v => File.Exists(Path.Combine(v, "um", "x64", "kernel32.lib")));
        }
        catch { return false; }
    }

    /// <summary>Makes sure the Windows SDK is installed (Build Tools installed by hand often lack it). True if present afterwards.</summary>
    public static async Task<bool> EnsureWindowsSdkAsync(Action<string> log, CancellationToken ct)
    {
        if (HasWindowsSdk()) return true;

        log("[..] The Windows SDK is missing (the linker needs kernel32.lib). Installing it...");
        await ModifyVsAsync("--add Microsoft.VisualStudio.Component.Windows11SDK.22621 --includeRecommended",
            "Adding the Windows SDK to Visual Studio Build Tools", log, ct);

        if (!HasWindowsSdk())
        {
            // No Visual Studio installer to modify: install the stand-alone SDK.
            try
            {
                await ProcessRunner.RunAsync("winget",
                    "install --id Microsoft.WindowsSDK.10.0.22621 --exact --accept-package-agreements --accept-source-agreements --silent",
                    null, Filter(log), ct);
            }
            catch (Win32Exception) { /* winget missing */ }
        }

        if (HasWindowsSdk()) { log("[ok] Windows SDK installed."); return true; }

        log("[!!] The Windows SDK could not be installed automatically. Open the Visual Studio Installer, click Modify on " +
            "Build Tools and tick \"Windows 11 SDK\" (or \"Windows 10 SDK\") under Desktop development with C++, then try again.");
        return false;
    }

    /// <summary>Path of vcvars64.bat of the newest Visual Studio / Build Tools install (any year or edition), or null.</summary>
    public static async Task<string?> FindVcvars64Async(CancellationToken ct)
    {
        foreach (var root in await VswhereAsync("-products * -prerelease -latest -property installationPath", ct))
        {
            var bat = Path.Combine(root, "VC", "Auxiliary", "Build", "vcvars64.bat");
            if (File.Exists(bat)) return bat;
        }
        return null;
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

        if (!await IsInstalledAsync(d, ct) && d.Check == "vsbuildtools")
        {
            await AddVcWorkloadAsync(log, ct);
            RefreshPath();
        }

        if (await IsInstalledAsync(d, ct))
        {
            log($"[ok] {d.Name} installed.");
            return true;
        }

        log($"[!!] {d.Name} could not be verified after installation. " +
            "If the installer finished, close and reopen the launcher, then try again. " +
            (d.Check == "vsbuildtools"
                ? "Open the Visual Studio Installer, click Modify on your Build Tools and tick \"Desktop development with C++\"."
                : ""));
        return false;
    }

    // winget prints spinners and progress bars; keep only lines with readable text.
    private static Action<string> Filter(Action<string> log) => line =>
    {
        var text = line.Trim();
        if (text.Length > 1 && text.Any(char.IsLetterOrDigit)) log(text);
    };
}
