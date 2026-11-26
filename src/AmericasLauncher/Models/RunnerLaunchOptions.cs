// SPDX-License-Identifier: GPL-2.0-only
namespace AmericasLauncher.Models;

/// <summary>
/// The user's own launch settings for one runner (saved in launch-options.json, see LaunchOptionsStore).
/// Applied only when that runner starts.
/// </summary>
public sealed class RunnerLaunchOptions
{
    private Dictionary<string, string> _env = new(StringComparer.OrdinalIgnoreCase);
    private string _extraArgs = "";

    /// <summary>Environment variables for the runner's processes. An empty value removes an inherited one.</summary>
    public Dictionary<string, string> Env { get => _env; set => _env = value ?? new(StringComparer.OrdinalIgnoreCase); }

    /// <summary>Arguments added after the runner's own launch arguments.</summary>
    public string ExtraArgs { get => _extraArgs; set => _extraArgs = value ?? ""; }

    public bool IsEmpty => Env.Count == 0 && string.IsNullOrWhiteSpace(ExtraArgs);
}
