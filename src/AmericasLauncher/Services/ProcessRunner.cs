// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.Text;

namespace AmericasLauncher.Services;

public static class ProcessRunner
{
    /// <summary>
    /// Runs a console process hidden and forwards every output line to <paramref name="log"/>.
    /// <paramref name="log"/> may be called from background threads.
    /// </summary>
    public static async Task<int> RunAsync(
        string fileName, string arguments, string? workingDirectory,
        Action<string> log, CancellationToken ct = default,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";   // never block waiting for git credentials
        if (environment != null) LaunchOptionsService.Apply(psi.Environment, environment);

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
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

    /// <summary>
    /// Runs a console process hidden and returns its stdout as a string (stderr is discarded).
    /// Returns an empty string on non-zero exit or error.
    /// </summary>
    public static async Task<string> RunAndCaptureAsync(
        string fileName, string arguments, string? workingDirectory,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        try
        {
            using var process = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0 ? sb.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
