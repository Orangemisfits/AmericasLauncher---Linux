// SPDX-License-Identifier: GPL-2.0-only
namespace AmericasLauncher.Models;

public sealed class AppConfig
{
    public string GameFolder { get; set; } = "";
    public string GameExe { get; set; } = "";
    public string SelectedRunnerId { get; set; } = "xcl";
    public bool UsePipeline { get; set; } = true;
    public bool AutoUpdate { get; set; } = false;

    /// <summary>
    /// Off (default): one "Install / Update" button downloads and builds a runner.
    /// On: "Update Source" only downloads and "Build" only builds, so new runner code can be checked in between.
    /// </summary>
    public bool ReviewBeforeBuild { get; set; } = false;
    public string? PendingUpdateNotes { get; set; }
    public string? PendingUpdateVersion { get; set; }
    public string CustomRunnersDir { get; set; } = "";
    public string CustomCacheDir { get; set; } = "";

    /// <summary>Runner id -> true when Xbox Series S is chosen (default is Series X).</summary>
    public Dictionary<string, bool> SeriesS { get; set; } = new();

    /// <summary>Runner id -> per-runner pipeline toggle (default is true).</summary>
    public Dictionary<string, bool> RunnerPipeline { get; set; } = new();

    /// <summary>Runner id -> per-runner resolution (540p, 720p, 1080p, 2K, 4K).</summary>
    public Dictionary<string, string> RunnerResolution { get; set; } = new();

    /// <summary>Runner id -> installed source revision (short git hash).</summary>
    public Dictionary<string, string> InstalledVersions { get; set; } = new();
}
