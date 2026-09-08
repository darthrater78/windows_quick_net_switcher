#nullable enable
using System;
using System.IO;

namespace QuickNetSwitcher;

// Absolute paths to the Windows helper executables this app launches.
//
// These are resolved from the system directories rather than handed to
// Process.Start as bare names. CreateProcess searches the calling application's
// own directory before System32, so a bare "netsh" would run an attacker-planted
// netsh.exe sitting beside this exe -- with administrator rights, since the app
// runs elevated. Both directories below are writable only by administrators.
public static class SystemPaths
{
    private static readonly string WindowsDir = ResolveWindowsDir();

    public static readonly string Netsh = Path.Combine(WindowsDir, "System32", "netsh.exe");

    // explorer.exe lives in the Windows directory itself, not System32.
    public static readonly string Explorer = Path.Combine(WindowsDir, "explorer.exe");

    private static string ResolveWindowsDir()
    {
        // Fall back to the default location rather than let an empty result degrade
        // these back to bare names. Deliberately not %SystemRoot%: an environment
        // variable is inherited from whoever launched us, and this is the one path
        // the fix depends on. A wrong path here fails closed -- netsh simply does
        // not start -- which is the safe direction.
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !string.IsNullOrEmpty(dir) ? dir : @"C:\Windows";
    }
}
