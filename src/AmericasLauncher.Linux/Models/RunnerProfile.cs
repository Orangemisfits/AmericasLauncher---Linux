// SPDX-License-Identifier: GPL-2.0-only
using System.Text.Json.Serialization;

namespace AmericasLauncher.Models;

/// <summary>
/// The Linux part of a runner in Data/runners.json (the "linux" object). The runners are Windows
/// programs: on Linux they are cross-compiled for x86_64-pc-windows-msvc and run through Wine/Proton.
/// </summary>
public sealed class LinuxRunnerBuild
{
    /// <summary>Requirement ids checked before building: git, cc, clang, llvm-ml, rust, cargo-xwin.</summary>
    [JsonPropertyName("dependencies")] public List<string> Dependencies { get; set; } = new();

    /// <summary>Commands run with bash inside the cloned source folder.</summary>
    [JsonPropertyName("buildCommands")] public List<string> BuildCommands { get; set; } = new();

    /// <summary>Built executable, relative to the source folder (forward slashes).</summary>
    [JsonPropertyName("exe")] public string Exe { get; set; } = "";

    /// <summary>Put dxcompiler.dll and dxil.dll (DirectX Shader Compiler) next to the executable.</summary>
    [JsonPropertyName("needsDxc")] public bool NeedsDxc { get; set; }
}

public sealed class RunnerProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("gitUrl")] public string GitUrl { get; set; } = "";

    /// <summary>Commands run before <see cref="LaunchArgs"/>. Supports {gameFolder} and {gameExe}.</summary>
    [JsonPropertyName("preLaunchArgs")] public List<string> PreLaunchArgs { get; set; } = new();

    /// <summary>Arguments for the built executable. Supports {gameFolder} and {gameExe}.</summary>
    [JsonPropertyName("launchArgs")] public string LaunchArgs { get; set; } = "";

    [JsonPropertyName("notes")] public string? Notes { get; set; }

    /// <summary>Null when the runner has no Linux build recipe yet.</summary>
    [JsonPropertyName("linux")] public LinuxRunnerBuild? Linux { get; set; }

    public override string ToString() => Name;
}
