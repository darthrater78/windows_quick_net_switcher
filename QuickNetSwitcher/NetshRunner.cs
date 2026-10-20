#nullable enable
using System;

namespace QuickNetSwitcher;

public static class NetshRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    // Null when netsh did not start or did not finish in time.
    public static ProcessResult? Run(params string[] arguments) =>
        ProcessRunner.Run(SystemPaths.Netsh, Timeout, arguments);
}
