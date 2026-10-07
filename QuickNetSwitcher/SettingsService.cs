#nullable enable
using System;
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

    // One of ThemeService.Accents; anything else is treated as the default.
    public string Accent { get; set; } = ThemeService.DefaultAccent;
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
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        // A missing, unreadable or corrupt file starts from defaults rather than
        // stopping the app.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
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
