using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AmericasLauncher.Linux.Models;

public enum RunnerHealthState
{
    NotInstalled,
    UpToDate,
    UpdateAvailable,
    Broken
}

public sealed class RunnerProfile : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("gitUrl")] public string GitUrl { get; set; } = "";
    [JsonPropertyName("buildCommands")] public List<string> BuildCommands { get; set; } = new();
    [JsonPropertyName("exe")] public string Exe { get; set; } = "";
    [JsonPropertyName("preLaunchArgs")] public List<string> PreLaunchArgs { get; set; } = new();
    [JsonPropertyName("launchArgs")] public string LaunchArgs { get; set; } = "";
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("supportsConsole")] public bool SupportsConsole { get; set; }
    [JsonPropertyName("usesConsoleEnv")] public bool UsesConsoleEnv { get; set; }

    private RunnerHealthState _healthState = RunnerHealthState.NotInstalled;

    [JsonIgnore]
    public RunnerHealthState HealthState
    {
        get => _healthState;
        set
        {
            if (_healthState == value) return;
            _healthState = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HealthState)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusColor)));
        }
    }

    [JsonIgnore]
    public string StatusColor => HealthState switch
    {
        RunnerHealthState.UpToDate => "#22C55E",
        RunnerHealthState.Broken => "#EF4444",
        RunnerHealthState.UpdateAvailable => "#EAB308",
        _ => "#0F172A"
    };
}

public sealed class AppConfig
{
    public string GameFolder { get; set; } = "";
    public string GameExe { get; set; } = "";
    public string SelectedRunnerId { get; set; } = "xcl";
    public bool UsePipeline { get; set; } = true;
    public string CustomRunnersDir { get; set; } = "";
    public string CustomCacheDir { get; set; } = "";
    public Dictionary<string, bool> SeriesS { get; set; } = new();
    public Dictionary<string, bool> RunnerPipeline { get; set; } = new();
    public Dictionary<string, string> RunnerResolution { get; set; } = new();
    public Dictionary<string, string> InstalledVersions { get; set; } = new();
}
