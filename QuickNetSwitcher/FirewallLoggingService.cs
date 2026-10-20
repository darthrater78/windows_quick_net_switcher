#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace QuickNetSwitcher;

public record FirewallLogging(bool LogDropped, bool LogAllowed, string RawPath, string? FilePath,
    int MaxSizeKb, bool SetByPolicy);

// Per-profile firewall logging. Read from the registry, where the values are numbers
// rather than netsh's localized words, and changed through netsh, the supported way.
public static class FirewallLoggingService
{
    private const string DefaultPath = @"%systemroot%\system32\LogFiles\Firewall\pfirewall.log";
    private const int DefaultSizeKb = 4096;

    private const string LocalRoot =
        @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
    private const string PolicyRoot = @"SOFTWARE\Policies\Microsoft\WindowsFirewall";

    // The only profile names that ever reach netsh or the registry path. Anything else
    // is refused rather than passed through.
    private static (string Netsh, string LocalKey, string PolicyKey)? Profile(string name) => name switch
    {
        "Domain" => ("domainprofile", "DomainProfile", "DomainProfile"),
        "Private" => ("privateprofile", "StandardProfile", "PrivateProfile"),
        "Public" => ("publicprofile", "PublicProfile", "PublicProfile"),
        _ => null
    };

    public static bool IsKnownProfile(string name) => Profile(name) != null;

    public static FirewallLogging Read(string profileName)
    {
        var profile = Profile(profileName);
        if (profile == null)
            return new FirewallLogging(false, false, DefaultPath, null, DefaultSizeKb, false);

        using var policy = Registry.LocalMachine.OpenSubKey($@"{PolicyRoot}\{profile.Value.PolicyKey}\Logging");
        using var local = Registry.LocalMachine.OpenSubKey($@"{LocalRoot}\{profile.Value.LocalKey}\Logging");

        // A Group Policy value wins over the local one, value by value. Paths are read
        // unexpanded: expansion would use this process's environment, which is
        // inherited from whoever launched it (ResolveLogPath expands them instead).
        const RegistryValueOptions raw = RegistryValueOptions.DoNotExpandEnvironmentNames;
        T Value<T>(string name, T fallback) =>
            policy?.GetValue(name, null, raw) is T p ? p
            : local?.GetValue(name, null, raw) is T l ? l
            : fallback;

        var setByPolicy = policy != null && (policy.GetValue("LogDroppedPackets") != null
                                             || policy.GetValue("LogSuccessfulConnections") != null);
        var rawPath = Value("LogFilePath", DefaultPath);

        return new FirewallLogging(
            Value("LogDroppedPackets", 0) == 1,
            Value("LogSuccessfulConnections", 0) == 1,
            rawPath,
            ResolveLogPath(rawPath),
            Value("LogFileSize", DefaultSizeKb),
            setByPolicy);
    }

    public static bool SetLogging(string profileName, bool dropped, bool enable)
    {
        var profile = Profile(profileName);
        if (profile == null) return false;

        var result = NetshRunner.Run(
            "advfirewall", "set", profile.Value.Netsh, "logging",
            dropped ? "droppedconnections" : "allowedconnections",
            enable ? "enable" : "disable");

        return result is { ExitCode: 0 }
               && !result.Output.Contains("Error", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string DefaultLogFolder =
        Path.Combine(SystemPaths.WindowsDir, "System32", "LogFiles", "Firewall");

    // Emptying a file with administrator rights is riskier than reading one: a path
    // redirected through a user-writable folder could empty a system file instead. So
    // only a log in Windows' own firewall log folder, which only administrators can
    // change, is ever cleared.
    public static bool CanClear(string path) =>
        string.Equals(Path.GetDirectoryName(path), DefaultLogFolder, StringComparison.OrdinalIgnoreCase);

    // Empties the log and removes its .old copy. Null on success, else why not.
    //
    // The firewall service may hold the log open in a way that refuses a truncate; then
    // logging to it is paused, the file emptied, and logging put back exactly as it was.
    public static string? ClearLog(string path)
    {
        if (!CanClear(path))
            return "only a log in Windows' firewall log folder can be cleared";

        var old = path + ".old";
        foreach (var file in new[] { path, old })
        {
            var info = new FileInfo(file);
            if (info.Exists && FirewallLogTail.HasLinkOnPath(info))
                return "the log path goes through a link";
        }

        if (!TryTruncate(path))
        {
            var paused = PauseLoggingTo(path);
            try
            {
                if (!TryTruncate(path))
                    return "the firewall service is still using the file";
            }
            finally
            {
                foreach (var (profile, dropped) in paused)
                    SetLogging(profile, dropped, true);
            }
        }

        try
        {
            if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"the log was cleared, but {Path.GetFileName(old)} could not be removed: {ex.Message}";
        }
        return null;
    }

    private static bool TryTruncate(string path)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (!File.Exists(path)) return true;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.SetLength(0);
                return true;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(200);
            }
        }
        return false;
    }

    private static List<(string Profile, bool Dropped)> PauseLoggingTo(string path)
    {
        var paused = new List<(string, bool)>();
        foreach (var profile in new[] { "Domain", "Private", "Public" })
        {
            var now = Read(profile);
            if (!string.Equals(now.FilePath, path, StringComparison.OrdinalIgnoreCase)) continue;
            if (now.LogDropped && SetLogging(profile, true, false)) paused.Add((profile, true));
            if (now.LogAllowed && SetLogging(profile, false, false)) paused.Add((profile, false));
        }
        foreach (var (profile, dropped) in paused)
            WaitForLogging(profile, dropped, false);
        return paused;
    }

    // netsh returns before the change is visible in the registry, so a read straight
    // after it can still show the old value. Waits up to 3 seconds for the new one.
    public static bool WaitForLogging(string profileName, bool dropped, bool enabled)
    {
        for (var i = 0; i < 15; i++)
        {
            var now = Read(profileName);
            if ((dropped ? now.LogDropped : now.LogAllowed) == enabled)
                return true;
            System.Threading.Thread.Sleep(200);
        }
        return false;
    }

    // The log path comes from firewall policy, which this app does not control, and the
    // app reads it with administrator rights. So only a file on a local fixed drive is
    // ever opened: a UNC or device path, a mapped network drive, or any environment
    // variable other than the Windows directory is refused. Opening a UNC path would
    // send this account's credentials to whatever server it names.
    public static string? ResolveLogPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var path = raw.Trim().Trim('"');
        foreach (var variable in new[] { "%systemroot%", "%windir%" })
        {
            if (path.StartsWith(variable, StringComparison.OrdinalIgnoreCase))
                path = SystemPaths.WindowsDir + path[variable.Length..];
        }

        if (path.Contains('%') || path.StartsWith(@"\\") || path.StartsWith("//")
            || !Path.IsPathFullyQualified(path))
            return null;

        try
        {
            path = Path.GetFullPath(path);
            var root = Path.GetPathRoot(path);
            if (root == null || root.Length != 3 || root[1] != ':')
                return null;
            return new DriveInfo(root).DriveType == DriveType.Fixed ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                       or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
