namespace QuickNetSwitcher;

public class FirewallViewModel
{
    public string Name { get; set; } = "";
    public string ProfileKey { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string Status => IsEnabled ? "On" : "Off";
    public string Description { get; set; } = "";

    public static FirewallViewModel FromProfile(FirewallProfile profile) => new()
    {
        Name = $"{profile.Name} Profile",
        ProfileKey = profile.Name,
        IsEnabled = profile.IsEnabled,
        Description = profile.Name switch
        {
            "Domain" => "Active when connected to a domain network",
            "Private" => "Active on trusted home or work networks",
            "Public" => "Active on public networks like Wi-Fi hotspots",
            _ => ""
        }
    };
}
