// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public sealed class EmulatorService
{
    private readonly GitHubReleaseClient _github = new();

    public bool IsInstalled(EmulatorProfile p) =>
        File.Exists(Path.Combine(Paths.EmulatorDir(p.Id), p.Exe));

    /// <summary>Downloads and installs the latest release. Returns false if already up to date.</summary>
    public async Task<bool> InstallOrUpdateAsync(
        EmulatorProfile p, AppConfig config, IProgress<string> log, IProgress<double> progress, CancellationToken ct = default)
    {
        log.Report($"{p.Name}: checking for the latest release...");
        var release = await _github.GetLatestAsync(p.Repo, ct);

        config.InstalledVersions.TryGetValue(p.Id, out var current);
        if (IsInstalled(p) && current == release.Tag)
        {
            log.Report($"Already up to date ({release.Tag}).");
            return false;
        }

        var asset = release.Assets.FirstOrDefault(a => Regex.IsMatch(a.Name, p.AssetPattern, RegexOptions.IgnoreCase))
            ?? throw new InvalidOperationException(
                $"No release asset matches the pattern '{p.AssetPattern}'. Check emulators.json.");

        var tmpDir = Path.Combine(Path.GetTempPath(), "AmericasLauncher_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var zip = Path.Combine(tmpDir, asset.Name);
            log.Report($"Downloading: {asset.Name} ({release.Tag})");
            await _github.DownloadAsync(asset.DownloadUrl, zip, progress, ct);

            var dest = Paths.EmulatorDir(p.Id);
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            Directory.CreateDirectory(dest);

            log.Report("Extracting files...");
            ZipFile.ExtractToDirectory(zip, dest);   // .NET 8 protects against zip-slip

            config.InstalledVersions[p.Id] = release.Tag;
            log.Report($"Installation complete: {release.Tag}");
            return true;
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* temp folder, not critical */ }
        }
    }
}
