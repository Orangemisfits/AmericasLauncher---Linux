// SPDX-License-Identifier: GPL-2.0-only
namespace AmericasLauncher.Models;

public sealed class AppConfig
{
    public string GameFolder { get; set; } = "";
    public string GameExe { get; set; } = "";
    public string SelectedRunnerId { get; set; } = "xcl";
    /// <summary>Apply each runner's recommended settings when it starts (your own Launch Options win over them).</summary>
    public bool UseRecommended { get; set; } = true;
    public bool AutoUpdate { get; set; } = false;

    /// <summary>
    /// On (default): "Update Source" only downloads and "Build" only builds, so new runner code can be checked in between.
    /// Off: one "Install / Update" button downloads and builds a runner.
    /// </summary>
    public bool ReviewBeforeBuild { get; set; } = true;
    public string? PendingUpdateNotes { get; set; }
    public string? PendingUpdateVersion { get; set; }
    public string CustomRunnersDir { get; set; } = "";
    public string CustomCacheDir { get; set; } = "";
    public string CustomRunnerGitUrl { get; set; } = "";

    /// <summary>Runner id -> true when Xbox Series S is chosen (legacy, migrated to ConsoleMode).</summary>
    public Dictionary<string, bool> SeriesS { get; set; } = new();

    /// <summary>Runner id -> "x", "s", or "off" (default "x").</summary>
    public Dictionary<string, string> ConsoleMode { get; set; } = new();

    /// <summary>Runner id -> "amd", "nvidia", or "off": the graphics card whose recommended settings are used.</summary>
    public Dictionary<string, string> RunnerGpu { get; set; } = new();

    /// <summary>Runner id -> installed source revision (short git hash).</summary>
    public Dictionary<string, string> InstalledVersions { get; set; } = new();
}
