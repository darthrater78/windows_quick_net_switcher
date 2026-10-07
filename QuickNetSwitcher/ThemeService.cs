#nullable enable
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace QuickNetSwitcher;

// Light/dark theming and the accent colour. The palette lives in slot 0 of the
// application's merged dictionaries (see App.xaml) and is replaced wholesale here;
// every consumer reaches for its colours with DynamicResource, so the swap
// propagates without rebuilding any window.
public static class ThemeService
{
    private const string PersonalizeKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKeyPath = @"SOFTWARE\Microsoft\Windows\DWM";

    public const string DefaultAccent = "Teal";
    public const string WindowsAccent = "Windows";

    /// <summary>Every accent the settings menu offers. All but Windows name a
    /// colour in the theme dictionaries (AccentTealColor and so on).</summary>
    public static readonly string[] Accents = { DefaultAccent, WindowsAccent, "Green", "Ink" };

    // WCAG minimums: body text, and the edge of a control or filled state.
    private const double TextContrast = 4.5;
    private const double ControlContrast = 3.0;

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

    /// <summary>The accent currently applied; always one of <see cref="Accents"/>.</summary>
    public static string Accent { get; private set; } = DefaultAccent;

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

    public static void Apply(bool dark, string? accent)
    {
        IsDark = dark;
        // A settings file edited by hand, or written by a later version, may name
        // an accent this build does not have.
        Accent = Accents.FirstOrDefault(a => a == accent) ?? DefaultAccent;

        var app = Application.Current;
        if (app == null) return;

        var palette = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml",
                UriKind.Absolute)
        };

        var dictionaries = app.Resources.MergedDictionaries;
        if (dictionaries.Count > 0)
            dictionaries[0] = palette;
        else
            dictionaries.Add(palette);

        ApplyAccent(app.Resources, palette);

        foreach (Window window in app.Windows)
            ApplyTitleBar(window);
    }

    // The three accent brushes are written straight into the application's own
    // dictionary, which is searched before the merged palette. They are rewritten on
    // every theme change, because each accent has a different value per theme.
    private static void ApplyAccent(ResourceDictionary resources, ResourceDictionary palette)
    {
        var card = ColorOf(palette, "CardBrush");
        var surface = ColorOf(palette, "SurfaceBrush");
        var text = ColorOf(palette, "TextPrimaryBrush");

        var fill = Accent == WindowsAccent
            ? WindowsAccentColor() ?? (Color)palette[$"Accent{DefaultAccent}Color"]
            : (Color)palette[$"Accent{Accent}Color"];

        // The named accents were chosen to pass against both surfaces. A Windows
        // accent is whatever the user set, so it is checked: one too close to the
        // row colour would make an on-state switch look like an empty one, and the
        // app falls back to the text colour rather than show that.
        if (Contrast(fill, card) < ControlContrast)
            fill = text;

        var asText = Math.Min(Contrast(fill, card), Contrast(fill, surface)) >= TextContrast
            ? fill
            : text;

        // Whichever of the theme's own row and text colours reads better on the fill.
        var onFill = Contrast(card, fill) >= Contrast(text, fill) ? card : text;

        resources["AccentBrush"] = Frozen(asText);
        resources["AccentFillBrush"] = Frozen(fill);
        resources["OnAccentBrush"] = Frozen(onFill);
    }

    private static Color ColorOf(ResourceDictionary palette, string brushKey) =>
        ((SolidColorBrush)palette[brushKey]).Color;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // The accent from Settings > Personalisation > Colours, stored as 0xAABBGGRR.
    // Absent on builds older than Windows 10, where the caller falls back.
    private static Color? WindowsAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmKeyPath, false);
            if (key?.GetValue("AccentColor") is not int abgr) return null;

            return Color.FromRgb(
                (byte)(abgr & 0xFF),
                (byte)((abgr >> 8) & 0xFF),
                (byte)((abgr >> 16) & 0xFF));
        }
        catch
        {
            return null;
        }
    }

    // WCAG 2.x contrast ratio between two opaque colours, from 1 to 21.
    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
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
