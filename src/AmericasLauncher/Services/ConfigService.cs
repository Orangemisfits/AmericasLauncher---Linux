// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.Text.Json;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

public static class Paths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AmericasLauncher");

    public static string ConfigFile => Path.Combine(Root, "config.json");
}

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Paths.ConfigFile)) ?? new AppConfig();
        }
        catch (JsonException) { /* corrupt file: fall back to defaults */ }
        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Paths.Root);
        File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(config, Options));
    }

    public static List<RunnerProfile> LoadProfiles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "runners.json");
        return JsonSerializer.Deserialize<List<RunnerProfile>>(File.ReadAllText(path)) ?? new();
    }
}
