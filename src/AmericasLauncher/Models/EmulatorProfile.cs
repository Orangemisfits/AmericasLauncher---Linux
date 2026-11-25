// SPDX-License-Identifier: GPL-2.0-only
using System.Text.Json.Serialization;

namespace AmericasLauncher.Models;

public sealed class EmulatorProfile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    [JsonPropertyName("assetPattern")] public string AssetPattern { get; set; } = "";
    [JsonPropertyName("exe")] public string Exe { get; set; } = "";
    [JsonPropertyName("launchArgs")] public string LaunchArgs { get; set; } = "\"{gamePath}\"";

    public override string ToString() => Name;
}
