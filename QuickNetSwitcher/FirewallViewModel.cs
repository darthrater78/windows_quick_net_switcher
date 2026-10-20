#nullable enable
namespace QuickNetSwitcher;

public class FirewallViewModel
{
    public string Name { get; set; } = "";
    public string ProfileKey { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string Status => IsEnabled ? "On" : "Off";
    public string Description { get; set; } = "";

    public bool LogDropped { get; set; }
    public bool LogAllowed { get; set; }
    public bool LoggingSetByPolicy { get; set; }
    public bool CanChangeLogging => !LoggingSetByPolicy;

    // Null when the configured path is not a local file this app will open.
    public string? LogFile { get; set; }
    public string LogFileName { get; set; } = "";
    public int LogMaxSizeKb { get; set; }

    public bool IsLogging => LogDropped || LogAllowed;
    public bool CanViewLog => IsLogging && LogFile != null;
    public bool LogPathRefused => IsLogging && LogFile == null;
    public string LogFileDisplay => $"{LogFileName} · {LogMaxSizeKb / 1024.0:0.#} MB max";

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
        },
        LogDropped = profile.Logging.LogDropped,
        LogAllowed = profile.Logging.LogAllowed,
        LoggingSetByPolicy = profile.Logging.SetByPolicy,
        LogFile = profile.Logging.FilePath,
        LogFileName = System.IO.Path.GetFileName(profile.Logging.RawPath),
        LogMaxSizeKb = profile.Logging.MaxSizeKb
    };
}
