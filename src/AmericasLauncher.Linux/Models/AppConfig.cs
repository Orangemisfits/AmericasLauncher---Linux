// SPDX-License-Identifier: GPL-2.0-only
using System.Text.Json.Serialization;

namespace AmericasLauncher.Models;

/// <summary>How the Windows runner executables are started on Linux.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WineRuntimeKind>))]
public enum WineRuntimeKind
{
    /// <summary>A Wine build (system <c>wine</c> or any custom wine binary). vkd3d-proton is installed into the prefix.</summary>
    Wine,

    /// <summary>Proton through umu-launcher (<c>umu-run</c>). Proton ships vkd3d-proton itself.</summary>
    Proton
}

public sealed class AppConfig
{
    public string GameFolder { get; set; } = "";
    public string GameExe { get; set; } = "";
    public string SelectedRunnerId { get; set; } = "xcl";

    /// <summary>Runner id -> installed source revision (short git hash).</summary>
    public Dictionary<string, string> InstalledVersions { get; set; } = new();

    public WineRuntimeKind Runtime { get; set; } = WineRuntimeKind.Wine;

    /// <summary>Wine binary: a name looked up in PATH ("wine") or an absolute path.</summary>
    public string WineCommand { get; set; } = "wine";

    /// <summary>umu PROTONPATH: "GE-Proton" (latest, downloaded by umu) or the folder of a Proton build.</summary>
    public string ProtonPath { get; set; } = "GE-Proton";

    /// <summary>Wine prefix; empty means the launcher's own prefix.</summary>
    public string WinePrefix { get; set; } = "";
}
