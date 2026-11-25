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
        Action<string> log, CancellationToken ct = default)
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

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
