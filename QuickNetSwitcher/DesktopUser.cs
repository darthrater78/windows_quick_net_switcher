#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace QuickNetSwitcher;

// The account signed in at the desktop, which is not always the account this process
// runs as. A standard user who elevates by typing an administrator's name and password
// gets an app running as that administrator, whose HKCU is the administrator's hive: its
// theme and accent are not the ones on screen. Per-user appearance settings are read
// from the desktop user's hive under HKEY_USERS instead, falling back to HKCU when that
// user can't be determined or the hive can't be read.
internal static class DesktopUser
{
    // wtsapi32 is not one of the KnownDLLs, so it is pinned to System32 like dwmapi:
    // a copy planted beside the exe must not load into this elevated process.
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void WTSFreeMemory(IntPtr memory);

    private const int WTSUserName = 5;
    private const int WTSDomainName = 7;

    private static readonly Lazy<string?> Sid = new(ResolveSid);

    /// <summary>Opens <paramref name="path"/> read-only in the desktop user's hive,
    /// or in HKCU when that is not possible. Null when neither has the key.</summary>
    public static RegistryKey? OpenKey(string path)
    {
        if (Sid.Value is { } sid)
        {
            try
            {
                var key = Registry.Users.OpenSubKey($@"{sid}\{path}", false);
                if (key != null) return key;
            }
            catch
            {
                // Hive unreadable from this account: fall through to our own.
            }
        }

        return Registry.CurrentUser.OpenSubKey(path, false);
    }

    // The user of this process's own session, as Windows Terminal Services reports it.
    // Null when the session has no user or the name doesn't translate to a SID.
    private static string? ResolveSid()
    {
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            var user = QueryString(session, WTSUserName);
            if (string.IsNullOrEmpty(user)) return null;
            var domain = QueryString(session, WTSDomainName);

            var account = string.IsNullOrEmpty(domain)
                ? new NTAccount(user)
                : new NTAccount(domain, user);
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));

            // A SID string is only ever S-, digits and dashes; anything else is not
            // used as a registry path.
            var value = sid.Value;
            foreach (var c in value)
                if (!(char.IsAsciiDigit(c) || c == '-' || c == 'S')) return null;
            return value;
        }
        catch
        {
            return null;
        }
    }

    private static string? QueryString(int session, int infoClass)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, session, infoClass, out var buffer, out _))
            return null;
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }
}
