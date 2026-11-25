// SPDX-License-Identifier: GPL-2.0-only
using System.Net.Http;
using System.Text.Json;

namespace AmericasLauncher.Services;

public sealed record ReleaseAsset(string Name, string DownloadUrl);
public sealed record ReleaseInfo(string Tag, IReadOnlyList<ReleaseAsset> Assets);

public sealed class GitHubReleaseClient
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AmericasLauncher/0.1");   // required by the GitHub API
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public async Task<ReleaseInfo> GetLatestAsync(string repo, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{repo}/releases/latest";
        using var resp = await Http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();   // 403 may mean rate limiting (60 requests/hour unauthenticated)

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(a => new ReleaseAsset(
                a.GetProperty("name").GetString() ?? "",
                a.GetProperty("browser_download_url").GetString() ?? ""))
            .ToList();
        return new ReleaseInfo(tag, assets);
    }

    public async Task DownloadAsync(string url, string destFile, IProgress<double>? progress, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destFile);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((double)read / total * 100);
        }
    }
}
