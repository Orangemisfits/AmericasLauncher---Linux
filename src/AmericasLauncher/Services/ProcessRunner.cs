// SPDX-License-Identifier: GPL-2.0-only
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AmericasLauncher.Services;

public static class ProcessRunner
{
    /// <summary>
    /// Git must never ask for a login. GIT_TERMINAL_PROMPT=0 only stops the console prompt: Git Credential Manager
    /// (installed with Git for Windows) still opened a GitHub sign-in window when a repository was not found or
    /// private (for example a deleted runner or a mistyped Custom Runner URL), and signing in did nothing.
    /// Every runner here is a public repository, so no credentials are used at all.
    /// </summary>
    public static void NoGitPrompts(IDictionary<string, string?> env)
    {
        env["GIT_TERMINAL_PROMPT"] = "0";        // no console prompt
        env["GCM_INTERACTIVE"] = "never";        // Git Credential Manager: no sign-in window
        env["GIT_ASKPASS"] = "echo";             // no askpass program either
        env["SSH_ASKPASS"] = "echo";
        env["GIT_CONFIG_COUNT"] = "1";           // credential.helper cleared for these git runs (git 2.31+)
        env["GIT_CONFIG_KEY_0"] = "credential.helper";
        env["GIT_CONFIG_VALUE_0"] = "";
    }

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
        NoGitPrompts(psi.Environment);
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
        NoGitPrompts(psi.Environment);

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

    /// <summary>
    /// Runs a console process hidden with redirected stdin, stdout, and stderr.
    /// Calls <paramref name="setInputSender"/> with a line-sending delegate while running, and null when done.
    /// Output is read as characters, not lines: a progress bar that only redraws one line with carriage
    /// returns (curl's <c>--progress-bar</c>) shows up in the log as it moves (at most about once a second).
    /// With <paramref name="closeInput"/> the process's input is closed right away: a <c>pause</c> or a prompt
    /// then returns at once instead of waiting for a key nobody can press.
    /// </summary>
    public static async Task<int> RunInteractiveAsync(
        string fileName, string arguments, string? workingDirectory,
        Action<string> log, Action<Action<string>?> setInputSender,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool closeInput = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        NoGitPrompts(psi.Environment);
        if (environment != null) LaunchOptionsService.Apply(psi.Environment, environment);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var pumps = new[]
        {
            Task.Run(() => PumpAsync(process.StandardOutput, log)),
            Task.Run(() => PumpAsync(process.StandardError, log))
        };

        if (closeInput)
        {
            try { process.StandardInput.Close(); } catch { /* process may have exited */ }
        }
        else
        {
            setInputSender(text =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.StandardInput.WriteLine(text);
                        process.StandardInput.Flush();
                    }
                }
                catch { /* process may have exited */ }
            });
        }

        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAny(Task.WhenAll(pumps), Task.Delay(2000, CancellationToken.None));   // drain what is left
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw;
        }
        finally
        {
            if (!closeInput) setInputSender(null);
        }
        return process.ExitCode;
    }

    /// <summary>
    /// Reads a process's output and passes it to <paramref name="log"/>: a line break ends a line;
    /// a lone carriage return (a progress bar redrawing itself) is shown at most about once a second.
    /// </summary>
    private static async Task PumpAsync(StreamReader reader, Action<string> log)
    {
        var line = new StringBuilder();
        var buffer = new char[1024];
        var lastProgress = DateTime.MinValue;
        var lastShown = "";
        var pendingCr = false;

        // curl's bar looks like "######      45.3%": shown as "download: 45.3%".
        static string Tidy(string text)
        {
            text = text.Trim();
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^#+\s*([0-9.]+)%$");
            return m.Success ? $"download: {m.Groups[1].Value}%" : text;
        }

        void Progress()
        {
            var text = Tidy(line.ToString());
            line.Clear();
            if (text.Length == 0 || text == lastShown) return;
            if (DateTime.UtcNow - lastProgress < TimeSpan.FromSeconds(1)) return;
            lastProgress = DateTime.UtcNow;
            lastShown = text;
            log("    " + text);
        }

        void EndLine()
        {
            log(Tidy(line.ToString()));
            line.Clear();
            lastShown = "";
        }

        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    var c = buffer[i];
                    if (pendingCr)
                    {
                        pendingCr = false;
                        if (c == '\n') { EndLine(); continue; }   // "\r\n" is one ordinary line break
                        Progress();                               // a lone "\r": the line is being redrawn
                    }
                    if (c == '\r') pendingCr = true;
                    else if (c == '\n') EndLine();
                    else line.Append(c);
                }
            }
            if (line.Length > 0) log(Tidy(line.ToString()));
        }
        catch { /* the process ended */ }
    }
}
