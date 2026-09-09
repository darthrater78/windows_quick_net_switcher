#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace QuickNetSwitcher;

public class AppSettings
{
    public bool PinToDesktop { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool SimpleView { get; set; } = false;
    public bool HideDisconnected { get; set; } = false;

    // null means "follow the Windows app theme", which is the default until the
    // user actually picks one. A plain bool could not express that: it would have
    // to default to light and would silently override the OS setting.
    public bool? DarkMode { get; set; }
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
        catch
        {
            return new();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFile)!;
            Directory.CreateDirectory(dir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, options));
        }
        catch
        {
        }
    }
}
