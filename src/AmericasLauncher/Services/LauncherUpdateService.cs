// SPDX-License-Identifier: GPL-2.0-only
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace AmericasLauncher.Services;

public sealed record LauncherUpdateResult(
    bool HasRelease,
    bool IsUpdateAvailable,
    string? LatestVersion,
    string? ReleaseUrl,
    string Message);

public sealed class LauncherUpdateService
{
    public const string CurrentVersionString = "1.2.0";
    private const string LatestReleaseApi = "https://api.github.com/repos/isaac-btw/AmericasLauncher/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
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
                Message: $"No releases published yet (current: v{CurrentVersionString}).");
        }

        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString()?.Trim() ?? "" : "";
        var url = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;
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
                    Message: $"Update available: {tag} (current: v{CurrentVersionString}).");
            }

            return new LauncherUpdateResult(
                HasRelease: true,
                IsUpdateAvailable: false,
                LatestVersion: tag,
                ReleaseUrl: url,
                Message: $"You are up to date (v{CurrentVersionString}).");
        }

        return new LauncherUpdateResult(
            HasRelease: true,
            IsUpdateAvailable: false,
            LatestVersion: tag,
            ReleaseUrl: url,
            Message: $"Latest release: {tag} (current: v{CurrentVersionString}).");
    }
}
