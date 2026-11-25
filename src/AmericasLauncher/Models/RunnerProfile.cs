// SPDX-License-Identifier: GPL-2.0-only
using System.Text.Json.Serialization;

namespace AmericasLauncher.Models;

public sealed class Dependency
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>winget package id, e.g. "Git.Git".</summary>
    [JsonPropertyName("wingetId")] public string WingetId { get; set; } = "";

    /// <summary>"command:&lt;exe&gt;" (found via PATH) or "vsbuildtools".</summary>
    [JsonPropertyName("check")] public string Check { get; set; } = "";

    /// <summary>Optional installer arguments passed through winget's --override.</summary>
    [JsonPropertyName("wingetOverride")] public string? WingetOverride { get; set; }
}

public sealed class RunnerProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("gitUrl")] public string GitUrl { get; set; } = "";
    [JsonPropertyName("dependencies")] public List<Dependency> Dependencies { get; set; } = new();

    /// <summary>Commands run (via cmd.exe) inside the cloned source folder.</summary>
    [JsonPropertyName("buildCommands")] public List<string> BuildCommands { get; set; } = new();

    /// <summary>Path of the built executable, relative to the source folder.</summary>
    [JsonPropertyName("exe")] public string Exe { get; set; } = "";

    /// <summary>Arguments for the built executable. Supports {gameFolder} and {gameExe}.</summary>
    [JsonPropertyName("launchArgs")] public string LaunchArgs { get; set; } = "";

    [JsonPropertyName("notes")] public string? Notes { get; set; }

    public override string ToString() => Name;
}
