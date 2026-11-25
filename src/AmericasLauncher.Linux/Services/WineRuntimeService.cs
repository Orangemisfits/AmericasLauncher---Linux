// SPDX-License-Identifier: GPL-2.0-only
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using AmericasLauncher.Models;
using ZstdSharp;

namespace AmericasLauncher.Services;

/// <summary>
/// Runs the Windows runner executables on Linux, through Wine (with vkd3d-proton installed into the
/// prefix for Direct3D 12) or through Proton with umu-launcher (which brings vkd3d-proton itself).
/// </summary>
public sealed partial class WineRuntimeService
{
    private sealed record ReleaseAsset(string Tag, string Name, string Url);

    // Used when the GitHub API cannot be reached (rate limit, offline mirror ...).
    private static readonly ReleaseAsset Vkd3dFallback = new(
        "v2.14.1", "vkd3d-proton-2.14.1.tar.zst",
        "https://github.com/HansKristian-Work/vkd3d-proton/releases/download/v2.14.1/vkd3d-proton-2.14.1.tar.zst");

    private static readonly ReleaseAsset DxcFallback = new(
        "v1.8.2505.1", "dxc_2025_07_14.zip",
        "https://github.com/microsoft/DirectXShaderCompiler/releases/download/v1.8.2505.1/dxc_2025_07_14.zip");

    private const string Vkd3dMarker = ".americas-launcher-vkd3d-proton";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AmericasLauncher/{LauncherUpdateService.CurrentVersionString}");
        return client;
    }

    // ---------------------------------------------------------------- configuration

    /// <summary>The configured prefix, else the launcher's own (one for Wine, one for Proton: Proton upgrades its prefix).</summary>
    public static string PrefixDir(AppConfig c) =>
        string.IsNullOrWhiteSpace(c.WinePrefix)
            ? (c.Runtime == WineRuntimeKind.Proton ? Paths.DefaultPrefix + "-proton" : Paths.DefaultPrefix)
            : Path.GetFullPath(LinuxEnvironment.ExpandHome(c.WinePrefix.Trim()));

    /// <summary>The program that runs Windows executables, or an explanation of what is missing.</summary>
    public static (string? Path, string Problem) ResolveRuntime(AppConfig c)
    {
        if (c.Runtime == WineRuntimeKind.Proton)
        {
            var umu = LinuxEnvironment.Which("umu-run");
            if (umu != null) return (umu, "");
            var hint = LinuxEnvironment.InstallHint(["umu-run"]);
            return (null, "umu-run (umu-launcher) was not found. " +
                          (hint != "" ? hint : "Install umu-launcher: https://github.com/Open-Wine-Components/umu-launcher") +
                          " — or switch to Wine in Settings.");
        }

        var command = string.IsNullOrWhiteSpace(c.WineCommand) ? "wine" : c.WineCommand.Trim();
        var wine = LinuxEnvironment.Which(command);
        if (wine != null) return (wine, "");
        return (null, $"Wine was not found ('{command}'). Install it ({LinuxEnvironment.InstallHint(["wine"])}), " +
                      "or set the path of a Wine build in Settings.");
    }

    /// <summary>Environment variables for every process started through Wine or Proton.</summary>
    public static Dictionary<string, string> RuntimeEnvironment(AppConfig c)
    {
        var env = new Dictionary<string, string>
        {
            ["WINEPREFIX"] = PrefixDir(c),
            // Wine's own messages drown the runner's log; WINEDEBUG set by the user wins.
            ["WINEDEBUG"] = System.Environment.GetEnvironmentVariable("WINEDEBUG") ?? "-all",
        };

        var userOverrides = System.Environment.GetEnvironmentVariable("WINEDLLOVERRIDES");
        if (c.Runtime == WineRuntimeKind.Wine)
        {
            // vkd3d-proton's d3d12.dll / d3d12core.dll from system32 instead of Wine's builtin ones.
            env["WINEDLLOVERRIDES"] = "d3d12,d3d12core=n,b" + (string.IsNullOrEmpty(userOverrides) ? "" : ";" + userOverrides);
        }
        else
        {
            env["GAMEID"] = System.Environment.GetEnvironmentVariable("GAMEID") ?? "umu-default";
            env["STORE"] = System.Environment.GetEnvironmentVariable("STORE") ?? "none";
            if (!string.IsNullOrWhiteSpace(c.ProtonPath))
                env["PROTONPATH"] = LinuxEnvironment.ExpandHome(c.ProtonPath.Trim());
        }
        return env;
    }

    /// <summary>The program and arguments that run <paramref name="exe"/> (a Linux path) with <paramref name="args"/>.</summary>
    public static (string FileName, List<string> Arguments) Command(string runtime, string exe, IEnumerable<string> args)
    {
        // `wine program.exe ...` and `umu-run program.exe ...` take the same form.
        var list = new List<string> { exe };
        list.AddRange(args);
        return (runtime, list);
    }

    public static bool IsPrefixReady(AppConfig c)
    {
        var prefix = PrefixDir(c);
        if (c.Runtime == WineRuntimeKind.Proton) return true;   // umu creates and updates its prefix itself
        return File.Exists(Path.Combine(prefix, "system.reg")) &&
               File.Exists(Path.Combine(prefix, "drive_c", "windows", "system32", "d3d12.dll")) &&
               File.Exists(Path.Combine(prefix, Vkd3dMarker));
    }

    // ---------------------------------------------------------------- prefix setup

    /// <summary>Creates the Wine prefix and installs vkd3d-proton into it (Wine), or checks umu-run (Proton).</summary>
    public async Task<bool> PrepareAsync(AppConfig c, Action<string> log, CancellationToken ct = default)
    {
        log("=== Preparing the Windows runtime ===");
        var (runtime, problem) = ResolveRuntime(c);
        if (runtime == null)
        {
            log("[!!] " + problem);
            return false;
        }

        var prefix = PrefixDir(c);
        if (c.Runtime == WineRuntimeKind.Proton)
        {
            log($"[ok] umu-run found: {runtime}");
            log($"     Proton: {(string.IsNullOrWhiteSpace(c.ProtonPath) ? "umu default" : c.ProtonPath)}, prefix: {prefix}");
            log("     umu downloads Proton and creates the prefix on the first launch (this can take a while). " +
                "Proton includes vkd3d-proton for Direct3D 12.");
            return true;
        }

        log($"[ok] Wine found: {runtime}");
        await LogWineVersionAsync(runtime, log, ct);

        if (!File.Exists(Path.Combine(prefix, "system.reg")))
        {
            log($"[..] Creating the Wine prefix in {prefix} ...");
            Directory.CreateDirectory(prefix);
            var env = RuntimeEnvironment(c);
            // No Wine Mono / Gecko install dialogs: the runners need neither.
            env["WINEDLLOVERRIDES"] = "mscoree,mshtml=";
            var code = await ProcessRunner.RunAsync(runtime, ["wineboot", "-u"], null, log, ct, env);
            if (code != 0 || !File.Exists(Path.Combine(prefix, "system.reg")))
            {
                log($"[!!] wineboot failed (exit code {code}). Check that Wine works: run 'wine --version' in a terminal.");
                return false;
            }
            log("[ok] Wine prefix created.");
        }
        else
        {
            log($"[ok] Wine prefix: {prefix}");
        }

        return await EnsureVkd3dAsync(prefix, log, ct);
    }

    private static async Task LogWineVersionAsync(string wine, Action<string> log, CancellationToken ct)
    {
        try
        {
            var (code, lines) = await ProcessRunner.CaptureAsync(wine, ["--version"], null, ct);
            // Debian's wrapper prints a wine32 notice first; the version line starts with "wine-".
            if (code == 0 && lines.FirstOrDefault(l => l.StartsWith("wine-", StringComparison.Ordinal)) is { } v) log("     " + v);
        }
        catch { /* informational only */ }
    }

    /// <summary>Installs (or updates) vkd3d-proton's 64-bit DLLs into the prefix's system32.</summary>
    private async Task<bool> EnsureVkd3dAsync(string prefix, Action<string> log, CancellationToken ct)
    {
        var system32 = Path.Combine(prefix, "drive_c", "windows", "system32");
        var marker = Path.Combine(prefix, Vkd3dMarker);
        var installed = File.Exists(marker) ? (await File.ReadAllTextAsync(marker, ct)).Trim() : "";

        var release = await LatestAssetAsync("HansKristian-Work/vkd3d-proton", VkdAssetRegex(), Vkd3dFallback, log, ct);
        if (installed == release.Tag && File.Exists(Path.Combine(system32, "d3d12.dll")))
        {
            log($"[ok] vkd3d-proton {installed} is installed in the prefix.");
            return true;
        }

        log($"[..] Installing vkd3d-proton {release.Tag} (Direct3D 12 on Vulkan) into the prefix ...");
        try
        {
            var archive = await DownloadCachedAsync(release, log, ct);
            Directory.CreateDirectory(system32);

            var wanted = new[] { "d3d12.dll", "d3d12core.dll" };
            var written = new List<string>();
            await using (var file = File.OpenRead(archive))
            await using (var zstd = new DecompressionStream(file))
            {
                var reader = new TarReader(zstd);
                while (await reader.GetNextEntryAsync(cancellationToken: ct) is { } entry)
                {
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
                    var parts = entry.Name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || parts[^2] != "x64" || !wanted.Contains(parts[^1])) continue;

                    var dest = Path.Combine(system32, parts[^1]);
                    await entry.ExtractToFileAsync(dest, overwrite: true, ct);
                    written.Add(parts[^1]);
                }
            }

            if (!written.Contains("d3d12.dll"))
            {
                log("[!!] The vkd3d-proton archive did not contain x64/d3d12.dll.");
                return false;
            }

            await File.WriteAllTextAsync(marker, release.Tag, ct);
            log($"[ok] vkd3d-proton {release.Tag} installed ({string.Join(", ", written)}).");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log("[!!] Could not install vkd3d-proton: " + ex.Message);
            return false;
        }
    }

    // ---------------------------------------------------------------- DirectX Shader Compiler

    /// <summary>Puts dxcompiler.dll and dxil.dll (from Microsoft's DXC release) into <paramref name="dir"/>.</summary>
    public async Task<bool> EnsureDxcAsync(string dir, Action<string> log, CancellationToken ct = default)
    {
        string[] dlls = ["dxcompiler.dll", "dxil.dll"];
        if (dlls.All(d => File.Exists(Path.Combine(dir, d))))
        {
            log("[ok] DirectX Shader Compiler (dxcompiler.dll, dxil.dll) is next to the runner.");
            return true;
        }

        try
        {
            var release = await LatestAssetAsync("microsoft/DirectXShaderCompiler", DxcAssetRegex(), DxcFallback, log, ct);
            var cache = Path.Combine(Paths.Tools, "dxc", release.Tag);
            if (!dlls.All(d => File.Exists(Path.Combine(cache, d))))
            {
                log($"[..] Downloading the DirectX Shader Compiler {release.Tag} from Microsoft ...");
                var zipPath = await DownloadCachedAsync(release, log, ct);
                Directory.CreateDirectory(cache);
                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var d in dlls)
                {
                    var entry = zip.GetEntry("bin/x64/" + d) ?? throw new InvalidDataException($"bin/x64/{d} is not in {release.Name}.");
                    entry.ExtractToFile(Path.Combine(cache, d), overwrite: true);
                }
            }

            Directory.CreateDirectory(dir);
            foreach (var d in dlls) File.Copy(Path.Combine(cache, d), Path.Combine(dir, d), overwrite: true);
            log($"[ok] DirectX Shader Compiler {release.Tag} placed next to the runner.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log("[!!] Could not set up the DirectX Shader Compiler: " + ex.Message);
            return false;
        }
    }

    // ---------------------------------------------------------------- downloads

    private static async Task<ReleaseAsset> LatestAssetAsync(
        string repo, Regex assetName, ReleaseAsset fallback, Action<string> log, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await Http.SendAsync(req, cts.Token);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (tag != "" && url != "" && assetName.IsMatch(name)) return new ReleaseAsset(tag, name, url);
            }
            log($"     {repo}: no matching file in the latest release, using {fallback.Tag}.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log($"     {repo}: could not read the latest release ({ex.Message}), using {fallback.Tag}.");
        }
        return fallback;
    }

    /// <summary>Downloads a release file into ~/.local/share/AmericasLauncher/tools/downloads (once).</summary>
    private static async Task<string> DownloadCachedAsync(ReleaseAsset asset, Action<string> log, CancellationToken ct)
    {
        var dir = Path.Combine(Paths.Tools, "downloads");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, asset.Name);
        if (File.Exists(path)) return path;

        var tmp = path + ".part";
        using (var resp = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            await src.CopyToAsync(dst, ct);
            log($"     downloaded {asset.Name}" + (total is { } t ? $" ({t / (1024 * 1024.0):0.0} MiB)" : ""));
        }
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    [GeneratedRegex(@"^vkd3d-proton-[0-9.]+\.tar\.zst$")]
    private static partial Regex VkdAssetRegex();

    [GeneratedRegex(@"^dxc_\d{4}_\d{2}_\d{2}\.zip$")]
    private static partial Regex DxcAssetRegex();
}
