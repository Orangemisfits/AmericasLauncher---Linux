// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace AmericasLauncher.Services;

public sealed record LauncherUpdateResult(
    bool HasRelease,
    bool IsUpdateAvailable,
    string? LatestVersion,
    string? ReleaseUrl,
    string? DownloadUrl,
    string? ReleaseNotes,
    string Message);

public sealed class LauncherUpdateService
{
    /// <summary>
    /// Version of the running launcher. It is read from the assembly, which gets it from
    /// &lt;Version&gt; in the .csproj (the release workflow sets it from the git tag),
    /// so it can never get out of sync with the release.
    /// </summary>
    public static readonly string CurrentVersionString = GetCurrentVersion();

    private static string GetCurrentVersion()
    {
        var v = typeof(LauncherUpdateService).Assembly.GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
    }

    private const string LatestReleaseApi = "https://api.github.com/repos/isaac-btw/AmericasLauncher/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"AmericasLauncher/{CurrentVersionString}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public async Task<LauncherUpdateResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(LatestReleaseApi, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return new LauncherUpdateResult(
                HasRelease: false,
                IsUpdateAvailable: false,
                LatestVersion: null,
                ReleaseUrl: null,
                DownloadUrl: null,
                ReleaseNotes: null,
                Message: $"No releases published yet (current: v{CurrentVersionString}).");
        }

        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString()?.Trim() ?? "" : "";
        var url = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
        var notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
        {
            // Releases are Windows-only: pick the win-x64 zip.
            var platformSuffix = "win-x64";
            foreach (var asset in assetsEl.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains(platformSuffix, StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out var dEl))
                {
                    downloadUrl = dEl.GetString();
                    break;
                }
            }
            // Fallback: any zip (pre-v1.7.0 single-asset releases)
            if (downloadUrl == null)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                        asset.TryGetProperty("browser_download_url", out var dEl))
                    {
                        downloadUrl = dEl.GetString();
                        break;
                    }
                }
            }
        }

        var normalized = tag.TrimStart('v', 'V');

        if (Version.TryParse(normalized, out var remoteVer) &&
            Version.TryParse(CurrentVersionString, out var currentVer))
        {
            if (remoteVer > currentVer)
            {
                return new LauncherUpdateResult(
                    HasRelease: true,
                    IsUpdateAvailable: true,
                    LatestVersion: tag,
                    ReleaseUrl: url,
                    DownloadUrl: downloadUrl,
                    ReleaseNotes: notes,
                    Message: $"Update available: {tag} (current: v{CurrentVersionString}).");
            }

            return new LauncherUpdateResult(
                HasRelease: true,
                IsUpdateAvailable: false,
                LatestVersion: tag,
                ReleaseUrl: url,
                DownloadUrl: downloadUrl,
                ReleaseNotes: notes,
                Message: $"You are up to date (v{CurrentVersionString}).");
        }

        return new LauncherUpdateResult(
            HasRelease: true,
            IsUpdateAvailable: false,
            LatestVersion: tag,
            ReleaseUrl: url,
            DownloadUrl: downloadUrl,
            ReleaseNotes: notes,
            Message: $"Latest release: {tag} (current: v{CurrentVersionString}).");
    }

    /// <summary>Writes an update problem to %LocalAppData%\AmericasLauncher\update.log. Never throws.</summary>
    public static void LogProblem(string text)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AmericasLauncher");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "update.log"), $"[{DateTime.Now}] v{CurrentVersionString}: {text}{Environment.NewLine}");
        }
        catch { /* logging must never break the launcher */ }
    }

    public async Task DownloadAndApplyUpdateAsync(string downloadUrl, Action<string> onStatus, CancellationToken ct = default)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "AmericasLauncherUpdate");
        if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        Directory.CreateDirectory(tempRoot);

        var zipPath = Path.Combine(tempRoot, "update.zip");
        var extractDir = Path.Combine(tempRoot, "extracted");

        onStatus("Downloading update...");
        using (var resp = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zipPath);
            await resp.Content.CopyToAsync(fs, ct);
        }

        onStatus("Extracting update...");
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        // If the zip wraps everything in a single subfolder, unwrap it
        var sourceDir = extractDir;
        if (!File.Exists(Path.Combine(extractDir, "AmericasLauncher.exe")))
        {
            var subDirs = Directory.GetDirectories(extractDir);
            if (subDirs.Length == 1 && File.Exists(Path.Combine(subDirs[0], "AmericasLauncher.exe")))
                sourceDir = subDirs[0];
            else
                throw new InvalidOperationException(
                    "AmericasLauncher.exe was not found in the downloaded zip. " +
                    "The wrong platform zip may have been downloaded. Please update manually.");
        }

        // Resolve the real install directory.
        // Environment.ProcessPath is the actual .exe on disk (correct even for single-file publish).
        // AppContext.BaseDirectory can point to a temp extraction folder for single-file apps.
        var exeOnDisk = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exeOnDisk) || !File.Exists(exeOnDisk))
            exeOnDisk = Path.Combine(AppContext.BaseDirectory, "AmericasLauncher.exe");

        var appDir = Path.GetDirectoryName(exeOnDisk)!.TrimEnd('\\', '/');
        var pid = Environment.ProcessId;
        var scriptPath = Path.Combine(tempRoot, "apply.cmd");

        // robocopy returns 0-7 for success (bit flags) and 8+ for real errors. Everything is logged to
        // %TEMP%\AmericasLauncher-update.log (outside the temp folder that gets deleted) so a failed update can be diagnosed;
        // the window is hidden, so the script must never wait for a keypress. "chcp 65001" keeps non-ASCII paths intact.
        var script = $""""
@echo off
chcp 65001 >NUL
set "LOG=%TEMP%\AmericasLauncher-update.log"
echo [%date% %time%] update started, waiting for PID {pid} to exit > "%LOG%"
set /a waits=0
:wait
tasklist /FI "PID eq {pid}" /FO CSV /NH 2>NUL | find """{pid}""" >NUL
if errorlevel 1 goto copy
set /a waits+=1
if %waits% GEQ 60 goto copy
timeout /t 1 /nobreak >NUL
goto wait
:copy
set /a tries=0
:retry
echo [%time%] copying {sourceDir} to {appDir}, attempt %tries% >> "%LOG%"
robocopy "{sourceDir}" "{appDir}" /E /IS /IT /R:3 /W:1 /NFL /NDL /NJH /NJS >> "%LOG%" 2>&1
if %ERRORLEVEL% LSS 8 goto done
set /a tries+=1
if %tries% GEQ 10 goto failed
timeout /t 2 /nobreak >NUL
goto retry
:failed
echo [%time%] COPY FAILED: the old version stays in place >> "%LOG%"
:done
echo [%time%] starting launcher >> "%LOG%"
start "" "{appDir}\AmericasLauncher.exe"
timeout /t 3 /nobreak >NUL
rmdir /S /Q "{tempRoot}" 2>NUL
"""";
        await File.WriteAllTextAsync(scriptPath, script, ct);

        onStatus("Restarting launcher...");
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
