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
