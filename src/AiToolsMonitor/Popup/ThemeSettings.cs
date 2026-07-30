using Microsoft.Win32;

namespace AiToolsMonitor.Popup;

internal readonly record struct ThemeSettings(
    bool IsDark,
    bool TransparencyEnabled,
    bool HighContrast)
{
    public static ThemeSettings Read()
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(path);

        bool isLight = key?.GetValue("AppsUseLightTheme") is int light ? light != 0 : true;
        bool transparencyEnabled =
            key?.GetValue("EnableTransparency") is not int transparency || transparency != 0;

        return new ThemeSettings(
            IsDark: !isLight,
            TransparencyEnabled: transparencyEnabled,
            HighContrast: SystemInformation.HighContrast);
    }

    public ThemePalette Palette => HighContrast
        ? new ThemePalette(
            SystemColors.Window,
            SystemColors.WindowText,
            SystemColors.GrayText,
            SystemColors.Control,
            SystemColors.WindowFrame,
            SystemColors.Highlight)
        : IsDark
            ? new ThemePalette(
                Color.FromArgb(0x1C, 0x14, 0x1A),
                Color.FromArgb(0xFA, 0xEB, 0xF2),
                Color.FromArgb(0xBE, 0xA0, 0xAF),
                Color.FromArgb(140, 42, 30, 40),
                Color.FromArgb(70, 180, 100, 130),
                Color.FromArgb(0xF5, 0x6E, 0xA0))
            : new ThemePalette(
                Color.FromArgb(0xFF, 0xF5, 0xF8),
                Color.FromArgb(0x2D, 0x14, 0x23),
                Color.FromArgb(0x6E, 0x46, 0x5A),
                Color.FromArgb(180, 255, 255, 255),
                Color.FromArgb(90, 230, 170, 195),
                Color.FromArgb(0xEB, 0x4B, 0x82));
}

internal readonly record struct ThemePalette(
    Color FallbackSurface,
    Color PrimaryText,
    Color SecondaryText,
    Color CardBackground,
    Color CardBorder,
    Color PinkAccent);
