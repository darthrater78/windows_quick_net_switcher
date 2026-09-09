#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace QuickNetSwitcher;

// Light/dark theming. The palette lives in slot 0 of the application's merged
// dictionaries (see App.xaml) and is replaced wholesale here; every consumer
// reaches for its colours with DynamicResource, so the swap propagates without
// rebuilding any window.
public static class ThemeService
{
    private const string PersonalizeKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // DWMWA_USE_IMMERSIVE_DARK_MODE. The attribute is 20 from Windows 10 20H1
    // onward; the earlier builds that support it at all number it 19. Try the
    // current one and fall back once, since a wrong attribute is just an error code.
    private const int DwmImmersiveDarkMode = 20;
    private const int DwmImmersiveDarkModeBefore20H1 = 19;

    // Pinned to System32: the default search order looks in the application's own
    // directory first, and dwmapi is not one of the KnownDLLs Windows protects, so a
    // copy planted beside the exe would be loaded into this elevated process. Same
    // vector as the bare-name netsh launch fixed in v1.2.1.
    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>The theme currently applied.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>
    /// Whether Windows itself is set to dark for apps. Used as the default when the
    /// user has never made an explicit choice.
    /// </summary>
    public static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, false);
            // AppsUseLightTheme is 0 for dark, 1 for light, and absent on older builds.
            return key?.GetValue("AppsUseLightTheme") is int useLight && useLight == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool dark)
    {
        IsDark = dark;

        var dictionaries = Application.Current?.Resources.MergedDictionaries;
        if (dictionaries == null) return;

        var palette = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml",
                UriKind.Absolute)
        };

        if (dictionaries.Count > 0)
            dictionaries[0] = palette;
        else
            dictionaries.Add(palette);

        foreach (Window window in Application.Current!.Windows)
            ApplyTitleBar(window);
    }

    /// <summary>
    /// Darkens the non-client area. The title bar is drawn by the OS, not WPF, so a
    /// dark window otherwise keeps a bright caption strip along its top edge.
    /// No-ops before the handle exists — call it from SourceInitialized or later.
    /// </summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var value = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
        catch
        {
            // Cosmetic only; an unsupported build just keeps the light caption.
        }
    }
}
