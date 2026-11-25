// SPDX-License-Identifier: GPL-2.0-only
namespace AmericasLauncher.Models;

public sealed class AppConfig
{
    public string GamePath { get; set; } = "";
    public string SelectedEmulatorId { get; set; } = "xenia-canary";

    /// <summary>Emulator id -> installed release tag (e.g. "v1.2.3").</summary>
    public Dictionary<string, string> InstalledVersions { get; set; } = new();
}
