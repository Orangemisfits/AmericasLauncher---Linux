// SPDX-License-Identifier: GPL-2.0-only
using System.IO;

namespace AmericasLauncher.Services;

public static class Paths
{
    /// <summary>~/.local/share/AmericasLauncher (or $XDG_DATA_HOME/AmericasLauncher).</summary>
    /// <remarks>
    /// Not Environment.GetFolderPath(LocalApplicationData): on Linux it returns "" while
    /// ~/.local/share does not exist yet (a new account), which would make every path relative.
    /// </remarks>
    public static readonly string Root = Path.Combine(DataHome(), "AmericasLauncher");

    private static string DataHome()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(LinuxEnvironment.Home, ".local", "share");
    }

    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string Sources => Path.Combine(Root, "sources");
    public static string Tools => Path.Combine(Root, "tools");
    public static string DefaultPrefix => Path.Combine(Root, "prefix");
}

public static class CrashLog
{
    public static void Write(Exception? ex)
    {
        var text = ex?.ToString() ?? "Unknown error";
        Console.Error.WriteLine("Americas Launcher crashed: " + text);
        try
        {
            Directory.CreateDirectory(Paths.Root);
            File.WriteAllText(Path.Combine(Paths.Root, "crash.log"), DateTime.Now + Environment.NewLine + text);
        }
        catch { /* logging must never crash the app */ }
    }
}

/// <summary>Helpers for the Linux host: PATH, command lookup, distribution hints, Wine paths.</summary>
public static class LinuxEnvironment
{
    public static string Home
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home) && Path.IsPathRooted(home)) return home;
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
            return string.IsNullOrWhiteSpace(profile) ? "/tmp" : profile;
        }
    }

    public static string CargoBin =>
        Path.Combine(Environment.GetEnvironmentVariable("CARGO_HOME") is { Length: > 0 } c ? c : Path.Combine(Home, ".cargo"), "bin");

    /// <summary>
    /// A launcher started from the desktop does not read ~/.profile, so rustup's ~/.cargo/bin is often
    /// missing from PATH. Debian and Ubuntu keep clang-cl and llvm-ml only in /usr/lib/llvm-N/bin.
    /// Add both so that the build finds them, without shadowing the system's own tools.
    /// </summary>
    public static void PreparePath()
    {
        var parts = (Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin")
            .Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (!parts.Contains(CargoBin)) parts.Insert(0, CargoBin);

        var llvm = NewestLlvmBinDir();
        if (llvm != null && !parts.Contains(llvm)) parts.Add(llvm);

        Environment.SetEnvironmentVariable("PATH", string.Join(':', parts));
    }

    private static string? NewestLlvmBinDir()
    {
        try
        {
            return Directory.GetDirectories("/usr/lib", "llvm-*")
                .Select(d => (Dir: d, Ok: int.TryParse(Path.GetFileName(d)["llvm-".Length..], out var v), Version: v))
                .Where(x => x.Ok && Directory.Exists(Path.Combine(x.Dir, "bin")))
                .OrderByDescending(x => x.Version)
                .Select(x => Path.Combine(x.Dir, "bin"))
                .FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Like `which`: an absolute path is checked as is, a name is searched in PATH.</summary>
    public static string? Which(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        if (command.Contains('/'))
        {
            var full = Path.GetFullPath(ExpandHome(command));
            return IsExecutable(full) ? full : null;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, command);
            if (IsExecutable(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsExecutable(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch { return false; }
    }

    public static string ExpandHome(string path) =>
        path == "~" ? Home : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Home, path[2..]) : path;

    /// <summary>Wine maps the Linux root to drive Z: in every prefix, Proton's included.</summary>
    public static string ToWindowsPath(string linuxPath) =>
        "Z:" + Path.GetFullPath(linuxPath).Replace('/', '\\');

    // ---------------------------------------------------------------- distribution hints

    public enum PackageManager { Unknown, Apt, Dnf, Pacman, Zypper }

    public static PackageManager DetectPackageManager()
    {
        var ids = new List<string>();
        try
        {
            foreach (var line in File.ReadAllLines("/etc/os-release"))
            {
                if (line.StartsWith("ID=", StringComparison.Ordinal) || line.StartsWith("ID_LIKE=", StringComparison.Ordinal))
                    ids.AddRange(line[(line.IndexOf('=') + 1)..].Trim('"', '\'').Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch { /* no os-release: fall back to the binaries */ }

        if (ids.Any(i => i is "debian" or "ubuntu") || Which("apt-get") != null) return PackageManager.Apt;
        if (ids.Any(i => i is "fedora" or "rhel" or "centos") || Which("dnf") != null) return PackageManager.Dnf;
        if (ids.Any(i => i is "arch" or "archlinux") || Which("pacman") != null) return PackageManager.Pacman;
        if (ids.Any(i => i.StartsWith("opensuse", StringComparison.Ordinal) || i == "suse") || Which("zypper") != null) return PackageManager.Zypper;
        return PackageManager.Unknown;
    }

    /// <summary>The distribution command that installs the given system packages (by requirement id).</summary>
    public static string InstallHint(IEnumerable<string> requirementIds)
    {
        var pm = DetectPackageManager();
        var packages = requirementIds.Select(id => PackageName(pm, id)).Where(p => p != null).Distinct().ToList();
        if (packages.Count == 0) return "";
        var list = string.Join(' ', packages);
        return pm switch
        {
            PackageManager.Apt => "sudo apt install " + list,
            PackageManager.Dnf => "sudo dnf install " + list,
            PackageManager.Pacman => "sudo pacman -S --needed " + list,
            PackageManager.Zypper => "sudo zypper install " + list,
            _ => "Install with your package manager: " + list
        };
    }

    private static string? PackageName(PackageManager pm, string id) => id switch
    {
        "git" => "git",
        "cc" => pm switch
        {
            PackageManager.Apt => "build-essential",
            PackageManager.Pacman => "base-devel",
            _ => "gcc"
        },
        "clang" => "clang",
        "llvm-ml" => "llvm",
        "lld" => "lld",
        "wine" => pm == PackageManager.Apt ? "wine64" : "wine",
        "umu-run" => pm switch
        {
            PackageManager.Pacman or PackageManager.Dnf => "umu-launcher",
            _ => null   // not packaged everywhere: see https://github.com/Open-Wine-Components/umu-launcher
        },
        _ => null
    };
}
