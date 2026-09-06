using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace QuickNetSwitcher;

public record FirewallProfile(string Name, bool IsEnabled);

public static class FirewallService
{
    public static List<FirewallProfile> GetProfiles()
    {
        var profiles = new List<FirewallProfile>();
        var output = RunNetsh("advfirewall show allprofiles state");

        string? currentProfile = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("Domain Profile", StringComparison.OrdinalIgnoreCase))
                currentProfile = "Domain";
            else if (line.StartsWith("Private Profile", StringComparison.OrdinalIgnoreCase))
                currentProfile = "Private";
            else if (line.StartsWith("Public Profile", StringComparison.OrdinalIgnoreCase))
                currentProfile = "Public";
            else if (currentProfile != null && line.StartsWith("State", StringComparison.OrdinalIgnoreCase))
            {
                var isOn = line.Contains("ON", StringComparison.OrdinalIgnoreCase);
                profiles.Add(new FirewallProfile(currentProfile, isOn));
                currentProfile = null;
            }
        }

        return profiles;
    }

    public static bool SetProfileState(string profileName, bool enable)
    {
        var state = enable ? "on" : "off";
        var output = RunNetsh($"advfirewall set {profileName.ToLower()}profile state {state}");
        return !output.Contains("Error", StringComparison.OrdinalIgnoreCase);
    }

    private static string RunNetsh(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return "";
        var result = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return result;
    }
}
