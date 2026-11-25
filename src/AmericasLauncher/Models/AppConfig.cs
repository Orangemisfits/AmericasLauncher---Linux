// SPDX-License-Identifier: GPL-2.0-only
namespace AmericasLauncher.Models;

public sealed class AppConfig
{
    public string GameFolder { get; set; } = "";
    public string GameExe { get; set; } = "";
    public string SelectedRunnerId { get; set; } = "xcl";
    public bool UsePipeline { get; set; } = true;
    public bool AutoUpdate { get; set; } = false;
    public string? PendingUpdateNotes { get; set; }
    public string? PendingUpdateVersion { get; set; }

    /// <summary>Runner id -> installed source revision (short git hash).</summary>
    public Dictionary<string, string> InstalledVersions { get; set; } = new();
}
