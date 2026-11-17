#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace QuickNetSwitcher;

public class AppSettings
{
    public bool PinToDesktop { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    // True shows one line per adapter with details on request; false is "Show all
    // details". True is the default.
    public bool SimpleView { get; set; } = true;
    public bool HideDisconnected { get; set; } = false;

    // null means "follow the Windows app theme", which is the default until the
    // user actually picks one. A plain bool could not express that: it would have
    // to default to light and would silently override the OS setting.
    public bool? DarkMode { get; set; }

    // Look up names for outside addresses in the firewall log viewer. Off by default:
    // a reverse lookup can tell the owner of an address that it was noticed.
    public bool ResolveOutsideNames { get; set; } = false;

    // Firewall logging this app turned on ("Domain/dropped"), turned off again when it
    // closes -- or on the next launch, if it did not close cleanly. Entries are only
    // ever used to turn logging off, so an edited file can't turn anything on.
    public List<string> LoggingToTurnOff { get; set; } = new();

    // One of ThemeService.Accents; anything else is treated as the default.
    public string Accent { get; set; } = ThemeService.DefaultAccent;

    // Named IPv4 settings the user can apply to any adapter. Plain text: addresses,
    // gateways and DNS servers are not secrets (user's call, 2026-10-31).
    public List<IpPreset> IpPresets { get; set; } = new();
}

public static class SettingsService
{
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuickNetSwitcher", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new();
            var json = File.ReadAllText(SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            settings.IpPresets = CleanPresets(settings.IpPresets);
            return settings;
        }
        // A missing, unreadable or corrupt file starts from defaults rather than
        // stopping the app.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    // Applying a preset runs netsh elevated, so a preset read from the file must pass
    // the same checks as one typed into the dialog. Anything else is dropped, as are
    // duplicate names and any past the limit.
    private static List<IpPreset> CleanPresets(List<IpPreset>? presets)
    {
        var kept = new List<IpPreset>();
        foreach (var p in presets ?? new())
        {
            if (p == null) continue;
            p.Name = (p.Name ?? "").Trim();
            if (IpSettingsService.ValidateName(p.Name, kept) != null) continue;
            if (kept.Exists(k => string.Equals(k.Name, p.Name, StringComparison.OrdinalIgnoreCase))) continue;
            if (IpSettingsService.Validate(p.ToConfig()) != null) continue;
            kept.Add(IpPreset.From(p.Name, p.ToConfig()));
        }
        return kept;
    }

    public static bool Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFile)!;
            Directory.CreateDirectory(dir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return true;
    }
}
