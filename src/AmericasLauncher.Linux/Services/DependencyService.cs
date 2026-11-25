// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.Net.Http;

namespace AmericasLauncher.Services;

/// <summary>
/// Build requirements on Linux. System packages (git, a C compiler, clang, LLVM) cannot be installed
/// without root, so the launcher names the command that installs them. Rust (rustup) and cargo-xwin
/// install into the user's home and are set up automatically, like winget does on Windows.
/// </summary>
public sealed class DependencyService
{
    private const string RustupInitUrl = "https://static.rust-lang.org/rustup/dist/x86_64-unknown-linux-gnu/rustup-init";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static string DisplayName(string id) => id switch
    {
        "git" => "Git",
        "cc" => "C compiler (cc)",
        "clang" => "Clang",
        "llvm-ml" => "LLVM tools (llvm-ml)",
        "rust" => "Rust (rustup)",
        "cargo-xwin" => "cargo-xwin",
        _ => id
    };

    /// <summary>Requirements the user installs with the distribution's package manager.</summary>
    public static bool IsSystemPackage(string id) => id is "git" or "cc" or "clang" or "llvm-ml";

    public static bool IsInstalled(string id) => id switch
    {
        "git" => LinuxEnvironment.Which("git") != null,
        "cc" => LinuxEnvironment.Which("cc") != null,
        "clang" => LinuxEnvironment.Which("clang") != null,
        "llvm-ml" => LinuxEnvironment.Which("llvm-ml") != null,
        "rust" => LinuxEnvironment.Which("rustup") != null && LinuxEnvironment.Which("cargo") != null,
        "cargo-xwin" => LinuxEnvironment.Which("cargo-xwin") != null,
        _ => false
    };

    /// <summary>Checks every requirement and installs the user-level ones. Returns true if all are available.</summary>
    public async Task<bool> EnsureAllAsync(IReadOnlyList<string> ids, Action<string> log, CancellationToken ct = default)
    {
        var missingSystem = new List<string>();
        foreach (var id in ids.Where(IsSystemPackage))
        {
            if (IsInstalled(id)) log($"[ok] {DisplayName(id)} found.");
            else
            {
                log($"[!!] {DisplayName(id)} is missing.");
                missingSystem.Add(id);
            }
        }

        if (missingSystem.Count > 0)
        {
            log("[!!] Install the missing packages, then click Install / Update again:");
            log("     " + LinuxEnvironment.InstallHint(missingSystem));
            return false;
        }

        foreach (var id in ids.Where(i => !IsSystemPackage(i)))
        {
            if (IsInstalled(id))
            {
                log($"[ok] {DisplayName(id)} found.");
                continue;
            }

            var ok = id switch
            {
                "rust" => await InstallRustupAsync(log, ct),
                "cargo-xwin" => await InstallCargoXwinAsync(log, ct),
                _ => false
            };
            if (!ok)
            {
                log($"[!!] {DisplayName(id)} could not be set up.");
                return false;
            }
            log($"[ok] {DisplayName(id)} installed.");
        }
        return true;
    }

    private static async Task<bool> InstallRustupAsync(Action<string> log, CancellationToken ct)
    {
        log("[..] Installing Rust with rustup into ~/.cargo (no root needed). This can take a few minutes.");
        Directory.CreateDirectory(Paths.Tools);
        var init = Path.Combine(Paths.Tools, "rustup-init");

        await using (var src = await Http.GetStreamAsync(RustupInitUrl, ct))
        await using (var dst = File.Create(init))
            await src.CopyToAsync(dst, ct);
        File.SetUnixFileMode(init, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var env = new Dictionary<string, string> { ["RUSTUP_INIT_SKIP_PATH_CHECK"] = "yes" };
        var code = await ProcessRunner.RunAsync(
            init, ["-y", "--profile", "minimal", "--default-toolchain", "stable"], null, log, ct, env);
        LinuxEnvironment.PreparePath();
        return code == 0 && IsInstalled("rust");
    }

    private static async Task<bool> InstallCargoXwinAsync(Action<string> log, CancellationToken ct)
    {
        log("[..] Installing cargo-xwin (cross-compiles the Windows runners). This compiles it once and can take a few minutes.");
        var code = await ProcessRunner.RunAsync("cargo", ["install", "--locked", "cargo-xwin"], null, log, ct);
        return code == 0 && IsInstalled("cargo-xwin");
    }
}
