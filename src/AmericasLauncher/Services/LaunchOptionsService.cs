// SPDX-License-Identifier: GPL-2.0-only
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AmericasLauncher.Models;

namespace AmericasLauncher.Services;

/// <summary>
/// Reads, writes and applies a runner's launch settings (environment variables and extra arguments).
/// Values may use {sourceDir}, {gameFolder}, {gameExe} and {file:relative\path}, the trimmed text of a file
/// inside the runner's source folder. Nothing here throws on bad input: problems become warnings.
/// </summary>
public static partial class LaunchOptionsService
{
    /// <summary>Windows' limit for one environment variable's value.</summary>
    public const int MaxValueLength = 32767;

    /// <summary>The most a {file:...} placeholder reads; anything bigger could not fit in a variable anyway.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    [GeneratedRegex(@"\{file:([^}]+)\}")]
    private static partial Regex FilePlaceholderRegex();

    /// <summary>
    /// Windows' own rule for a variable name: not empty, no '=' and no null character (so names like
    /// ProgramFiles(x86) are fine). Leading or trailing spaces are not allowed, to catch typing slips.
    /// </summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 1024 && name.Trim() == name &&
        name.IndexOfAny(['=', '\0']) < 0;

    /// <summary>
    /// Why a value cannot be passed to a process, or null when it is fine. Line breaks are allowed (a
    /// {file:...} list can span lines, e.g. the nvidia runner's ray-tracing skip list); a null character or a
    /// value over Windows' limit would make starting the process fail.
    /// </summary>
    public static string? CheckValue(string? value)
    {
        if (value == null) return null;
        if (value.Length > MaxValueLength) return $"Too long ({value.Length:N0} characters, the limit is {MaxValueLength:N0}).";
        if (value.Contains('\0')) return "Null characters are not allowed.";
        return null;
    }

    /// <summary>
    /// Parses "NAME=value" lines. Blank lines and lines starting with '#' are ignored; a later line for the
    /// same name replaces an earlier one, at the later position. Problems are returned in <paramref name="errors"/>.
    /// </summary>
    public static Dictionary<string, string> ParseEnv(string? text, out List<string> errors)
    {
        errors = new List<string>();
        var last = new Dictionary<string, (int Order, string Name, string Value)>(StringComparer.OrdinalIgnoreCase);
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                errors.Add($"Line {i + 1}: expected NAME=value.");
                continue;
            }

            var name = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (!IsValidName(name))
            {
                errors.Add($"Line {i + 1}: \"{Shorten(name)}\" is not a valid name.");
                continue;
            }
            if (CheckValue(value) is { } problem)
            {
                errors.Add($"Line {i + 1}: {problem}");
                continue;
            }

            last[name] = (i, name, value);
        }

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, name, value) in last.Values.OrderBy(e => e.Order)) env[name] = value;
        return env;
    }

    /// <summary>The runner's launch arguments followed by the user's extra arguments.</summary>
    public static string ComposeArgs(string launchArgs, RunnerLaunchOptions? options)
    {
        var extra = options?.ExtraArgs?.Trim() ?? "";
        if (extra == "") return launchArgs;
        return string.IsNullOrWhiteSpace(launchArgs) ? extra : launchArgs.TrimEnd() + " " + extra;
    }

    /// <summary>
    /// The environment variables to apply, with placeholders filled in. A variable that cannot be used (bad
    /// name, unreadable file, too long, a null character) is left out with a warning. A null value means "remove".
    /// </summary>
    public static Dictionary<string, string?> ResolveEnv(
        RunnerLaunchOptions? options, string sourceDir, string gameFolder, string gameExe, Action<string> warn)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (options == null) return result;

        foreach (var (name, raw) in options.Env)
        {
            if (!IsValidName(name))
            {
                warn($"[!] Launch option \"{Shorten(name)}\" skipped: not a valid variable name.");
                continue;
            }
            if (string.IsNullOrEmpty(raw))
            {
                result[name] = null;
                continue;
            }

            var value = ExpandValue(raw, sourceDir, gameFolder, gameExe, out var problem);
            problem ??= CheckValue(value);
            if (problem != null)
            {
                warn($"[!] Launch option {name} skipped: {problem}");
                continue;
            }
            result[name] = value;
        }

        return result;
    }

    /// <summary>
    /// Fills in the placeholders of one value. The simple ones first, then {file:...}, so the text read from a
    /// file is used exactly as it is. <paramref name="problem"/> is set when a file cannot be used.
    /// </summary>
    public static string ExpandValue(string raw, string sourceDir, string gameFolder, string gameExe, out string? problem)
    {
        string? fileProblem = null;
        var root = Path.GetFullPath(sourceDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

        var simple = raw
            .Replace("{sourceDir}", sourceDir.TrimEnd('\\', '/'))
            .Replace("{gameFolder}", gameFolder.TrimEnd('\\', '/'))
            .Replace("{gameExe}", gameExe);

        var value = FilePlaceholderRegex().Replace(simple, m =>
        {
            var relative = m.Groups[1].Value.Trim();
            try
            {
                var path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    fileProblem ??= $"{{file:{relative}}} points outside the runner's folder.";
                    return "";
                }
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    fileProblem ??= $"file not found: {path}";
                    return "";
                }
                if (info.Length > MaxFileBytes)
                {
                    fileProblem ??= $"{{file:{relative}}} is too big ({info.Length:N0} bytes).";
                    return "";
                }
                return File.ReadAllText(path).Trim();
            }
            catch (Exception ex)
            {
                fileProblem ??= $"{{file:{relative}}} could not be read: {ex.Message}";
                return "";
            }
        });

        problem = fileProblem;
        return value;
    }

    /// <summary>Applies resolved variables to a process's environment.</summary>
    public static void Apply(IDictionary<string, string?> target, IReadOnlyDictionary<string, string?> env)
    {
        foreach (var (name, value) in env)
        {
            if (value == null) target.Remove(name);
            else target[name] = value;
        }
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "...";
}
