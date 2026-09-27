#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace QuickNetSwitcher;

public record NetshResult(int ExitCode, string Output);

// Every netsh launch goes through here: from its absolute path (see SystemPaths), with
// the arguments passed as a list rather than one string, so an adapter name reaches
// netsh as a single argument whatever it contains.
public static class NetshRunner
{
    private const int TimeoutMs = 5000;

    // Null when netsh did not start or did not finish in time.
    public static NetshResult? Run(params string[] arguments)
    {
        var psi = new ProcessStartInfo(SystemPaths.Netsh)
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
        // pipe fills would leave netsh blocked on that write, never exiting.
        var output = proc.StandardOutput.ReadToEndAsync();
        _ = proc.StandardError.ReadToEndAsync();

        // ExitCode throws on a process that is still running, which used to escape as an
        // unhandled exception from the metric dialog and take the app down. A netsh that
        // hangs is killed and reported as a failure instead.
        if (!proc.WaitForExit(TimeoutMs))
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

        return new NetshResult(proc.ExitCode, output.Result);
    }
}
