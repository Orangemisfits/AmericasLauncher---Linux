// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.Text.Json;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Paths.ConfigFile), Options) ?? new AppConfig();
        }
        catch (JsonException) { /* corrupt file: fall back to defaults */ }
        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Paths.Root);
        var tmp = Paths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, Paths.ConfigFile, overwrite: true);
    }

    public static List<RunnerProfile> LoadProfiles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "runners.json");
        var options = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonSerializer.Deserialize<List<RunnerProfile>>(File.ReadAllText(path), options) ?? new();
    }
}
