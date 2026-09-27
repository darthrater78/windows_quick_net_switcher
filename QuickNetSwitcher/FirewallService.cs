#nullable enable
using System;
using System.Collections.Generic;

namespace QuickNetSwitcher;

public record FirewallProfile(string Name, bool IsEnabled);

public static class FirewallService
{
    public static List<FirewallProfile> GetProfiles()
    {
        var profiles = new List<FirewallProfile>();
        var output = NetshRunner.Run("advfirewall", "show", "allprofiles", "state")?.Output ?? "";

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
        var result = NetshRunner.Run(
            "advfirewall", "set", $"{profileName.ToLowerInvariant()}profile", "state", state);

        // A netsh that never started or timed out used to read as success here, since
        // its empty output contains no "Error".
        return result != null && !result.Output.Contains("Error", StringComparison.OrdinalIgnoreCase);
    }
}
