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
    public const string CurrentVersionString = "1.5.0";
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

        var appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var exePath = Environment.ProcessPath ?? Path.Combine(appDir, "AmericasLauncher.exe");
        var pid = Environment.ProcessId;
        var scriptPath = Path.Combine(tempRoot, "apply.cmd");

        var script = $"""
@echo off
:wait
tasklist /FI "PID eq {pid}" 2>NUL | find /I "{pid}" >NUL
if "%ERRORLEVEL%"=="0" (
    timeout /t 1 /nobreak >NUL
    goto wait
)
xcopy /E /Y /I "{extractDir}\*" "{appDir}\" >NUL
start "" "{exePath}"
rmdir /S /Q "{tempRoot}"
""";
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
