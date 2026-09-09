#nullable enable
using Microsoft.Win32;

namespace QuickNetSwitcher;

// v1.1.0 through v1.2.1 offered a "Start with Windows" option that wrote a value under
// HKCU\...\CurrentVersion\Run. It never worked: the shell launches Run entries
// unelevated, and this app is manifested requireAdministrator, so Windows silently
// dropped the entry at every logon.
//
// The feature was removed in v1.3.0 rather than reimplemented. Auto-starting an
// elevated, unsigned app means a silent elevation path -- anyone who can overwrite the
// exe (trivial while it lives in Downloads or another non-admin-writable folder) gets
// administrator on the next logon, with no UAC prompt to notice. That trade is not
// worth a convenience toggle.
//
// This removes the leftover value so upgrading users are not left with a stale autorun
// entry pointing at the app. It can be deleted once v1.2.1 is far enough behind.
public static class LegacyStartupCleanup
{
    private const string ValueName = "QuickNetSwitcher";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static void RemoveRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            key?.DeleteValue(ValueName, false);
        }
        catch
        {
            // Nothing actionable: the entry is inert either way.
        }
    }
}
