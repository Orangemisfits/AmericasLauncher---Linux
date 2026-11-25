// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;

namespace AmericasLauncher.Services;

public static class ProcessRunner
{
    /// <summary>
    /// Starts a process with its output redirected, each line forwarded to <paramref name="log"/>
    /// (possibly from background threads). Arguments are passed one by one: no shell quoting involved.
    /// </summary>
    public static Process Start(
        string fileName, IEnumerable<string> arguments, string? workingDirectory,
        Action<string> log, IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,   // nothing can block waiting for a terminal answer
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";   // never block waiting for git credentials
        if (environment != null)
            foreach (var (k, v) in environment) psi.Environment[k] = v;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };

        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>Runs a process to completion and returns its exit code.</summary>
    public static async Task<int> RunAsync(
        string fileName, IEnumerable<string> arguments, string? workingDirectory,
        Action<string> log, CancellationToken ct = default, IReadOnlyDictionary<string, string>? environment = null)
    {
        using var process = Start(fileName, arguments, workingDirectory, log, environment);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw;
        }
        return process.ExitCode;
    }

    /// <summary>Runs one command line with bash (build steps from runners.json).</summary>
    public static Task<int> RunShellAsync(
        string command, string workingDirectory, Action<string> log,
        CancellationToken ct = default, IReadOnlyDictionary<string, string>? environment = null) =>
        RunAsync("/bin/bash", ["-c", command], workingDirectory, log, ct, environment);

    /// <summary>Runs a process and returns its trimmed output lines (stdout and stderr).</summary>
    public static async Task<(int Code, List<string> Lines)> CaptureAsync(
        string fileName, IEnumerable<string> arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        var lines = new List<string>();
        var code = await RunAsync(fileName, arguments, workingDirectory, s => { lock (lines) lines.Add(s.Trim()); }, ct);
        lock (lines) return (code, lines.ToList());
    }

    /// <summary>
    /// Splits a Windows-style argument string (runners.json "launchArgs"/"preLaunchArgs") into
    /// arguments: whitespace separates, double quotes group, backslashes are kept as they are
    /// (they are Windows path separators here).
    /// </summary>
    public static List<string> SplitArguments(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (hasToken) args.Add(current.ToString());
                current.Clear();
                hasToken = false;
            }
            else
            {
                current.Append(ch);
                hasToken = true;
            }
        }
        if (hasToken) args.Add(current.ToString());
        return args;
    }
}
