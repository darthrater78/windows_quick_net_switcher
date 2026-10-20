#nullable enable
using System;
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
