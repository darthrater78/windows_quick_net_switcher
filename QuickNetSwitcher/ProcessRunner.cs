#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace QuickNetSwitcher;

public record ProcessResult(int ExitCode, string Output);

// Every helper this app launches goes through here: from an absolute path (see
// SystemPaths), with the arguments passed as a list rather than one string, so a value
// reaches the helper as a single argument whatever it contains.
public static class ProcessRunner
{
    // Null when the helper did not start or did not finish in time.
    public static ProcessResult? Run(string exePath, TimeSpan timeout, params string[] arguments)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var proc = Process.Start(psi);
        if (proc == null) return null;

        // Both streams are drained at once. Reading one to the end while the other's
        // pipe fills would leave the helper blocked on that write, never exiting.
        var output = proc.StandardOutput.ReadToEndAsync();
        _ = proc.StandardError.ReadToEndAsync();

        // ExitCode throws on a process that is still running, which used to escape as an
        // unhandled exception from the metric dialog and take the app down. A helper
        // that hangs is killed and reported as a failure instead.
        if (!proc.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Exited between the timeout and the kill, or could not be killed; either
                // way it is reported as a failure.
            }
            return null;
        }

        return new ProcessResult(proc.ExitCode, output.Result);
    }
}
