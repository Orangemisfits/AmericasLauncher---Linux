// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;

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
}
