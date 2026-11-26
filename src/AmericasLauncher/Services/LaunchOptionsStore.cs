// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.Text.Json;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

/// <summary>
/// The user's per-runner launch options, kept in their own file (launch-options.json next to config.json):
/// launcher versions without this feature never rewrite it, and a problem with one file cannot take the other
/// with it. Nothing here throws: a damaged file is kept aside under another name, and a file that cannot be
/// opened is left untouched (saving is then refused instead of writing over it).
/// </summary>
public sealed class LaunchOptionsStore
{
    public static string FilePath => Path.Combine(Paths.Root, "launch-options.json");

    private Dictionary<string, RunnerLaunchOptions> _runners = new(StringComparer.OrdinalIgnoreCase);

    // Runners changed since the last successful save. A save writes only these on top of the file as it is on
    // disk, so another launcher window's changes to other runners (or later changes to the same runner) survive.
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Set when the file could not be used at startup; says what happened to it.</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>Something the last save had to do that the user should know about (or null). Read once.</summary>
    public string? TakeNotice()
    {
        var notice = _notice;
        _notice = null;
        return notice;
    }
    private string? _notice;

    /// <summary>
    /// False when the file exists but could not be opened at startup (e.g. locked by another program):
    /// saving would overwrite it, so nothing is written until the launcher is restarted.
    /// </summary>
    public bool CanSave { get; private set; } = true;

    public RunnerLaunchOptions? Get(string runnerId) =>
        _runners.TryGetValue(runnerId, out var options) && !options.IsEmpty ? options : null;

    /// <summary>Stores a runner's options in memory (empty options remove it). Call <see cref="Save"/> to write.</summary>
    public void Set(string runnerId, RunnerLaunchOptions options)
    {
        if (options.IsEmpty) _runners.Remove(runnerId);
        else _runners[runnerId] = options;
        _changed.Add(runnerId);
    }

    public static LaunchOptionsStore Load() => Load(FilePath);

    public static LaunchOptionsStore Load(string path)
    {
        var store = new LaunchOptionsStore();
        switch (Read(path, out var runners, out var error))
        {
            case ReadResult.Ok:
                store._runners = runners;
                break;
            case ReadResult.CannotOpen:
                store.CanSave = false;
                store.LoadProblem = $"Launch options could not be opened ({error}). Changes will not be saved until the launcher is restarted.";
                break;
            case ReadResult.Damaged:
                if (SetAside(path, out var aside))
                    store.LoadProblem = $"Launch options could not be read ({error}). The file was kept as {aside}.";
                else
                {
                    store.CanSave = false;
                    store.LoadProblem = $"Launch options could not be read ({error}). Changes will not be saved until the launcher is restarted.";
                }
                break;
        }
        return store;
    }

    /// <summary>
    /// Writes the runners changed since the last save on top of the file as it is on disk now (to a temporary
    /// file first, then swapped in). Returns an error message, or null.
    /// </summary>
    public string? Save() => Save(FilePath);

    public string? Save(string path)
    {
        if (!CanSave) return LoadProblem ?? "launch options are read-only until the launcher is restarted.";

        Dictionary<string, RunnerLaunchOptions> merged;
        switch (Read(path, out var onDisk, out var error))
        {
            case ReadResult.CannotOpen:
                return error;   // try again on the next change
            case ReadResult.Damaged:
                // Damaged since startup: keep it aside and write everything this window knows.
                _notice = SetAside(path, out var aside)
                    ? $"The launch options file was damaged; it was kept as {aside} and rewritten."
                    : null;
                if (_notice == null) return $"the launch options file is damaged and could not be moved aside ({error}).";
                merged = new Dictionary<string, RunnerLaunchOptions>(_runners, StringComparer.OrdinalIgnoreCase);
                break;
            default:
                merged = onDisk;
                foreach (var id in _changed)
                {
                    if (_runners.TryGetValue(id, out var mine)) merged[id] = mine;
                    else merged.Remove(id);
                }
                break;
        }

        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(temp))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteStartObject("runners");
                foreach (var (id, options) in merged.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
                {
                    writer.WriteStartObject(id);
                    writer.WriteStartObject("env");
                    foreach (var (name, value) in options.Env) writer.WriteString(name, value);
                    writer.WriteEndObject();
                    writer.WriteString("extraArgs", options.ExtraArgs);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            return ex.Message;
        }

        // In step with the file now, including what another window saved.
        _runners = merged;
        _changed.Clear();
        return null;
    }

    private enum ReadResult { Ok, CannotOpen, Damaged }

    /// <summary>Reads the file leniently, entry by entry (a value of an unexpected type is skipped). Never throws.</summary>
    private static ReadResult Read(string path, out Dictionary<string, RunnerLaunchOptions> runners, out string? error)
    {
        runners = new Dictionary<string, RunnerLaunchOptions>(StringComparer.OrdinalIgnoreCase);
        error = null;

        string text;
        try
        {
            if (!File.Exists(path)) return ReadResult.Ok;
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            error = ex.Message;   // locked, no permission, a folder ...
            return ReadResult.CannotOpen;
        }

        try
        {
            using var doc = JsonDocument.Parse(text,
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("runners", out var list) ||
                list.ValueKind != JsonValueKind.Object)
                return ReadResult.Ok;

            foreach (var runner in list.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(runner.Name) || runner.Value.ValueKind != JsonValueKind.Object) continue;
                var options = new RunnerLaunchOptions();

                if (runner.Value.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
                {
                    foreach (var variable in env.EnumerateObject())
                    {
                        var value = AsText(variable.Value);
                        if (variable.Name.Length > 0 && value != null) options.Env[variable.Name] = value;
                    }
                }
                if (runner.Value.TryGetProperty("extraArgs", out var args)) options.ExtraArgs = AsText(args) ?? "";

                if (!options.IsEmpty) runners[runner.Name] = options;
            }
            return ReadResult.Ok;
        }
        catch (Exception ex)
        {
            runners.Clear();
            error = ex.Message;
            return ReadResult.Damaged;
        }
    }

    private static bool SetAside(string path, out string asideName)
    {
        var aside = $"{path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
        asideName = Path.GetFileName(aside);
        try
        {
            File.Move(path, aside);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? AsText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => null
    };
}
