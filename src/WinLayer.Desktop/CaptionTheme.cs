using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinLayer.Core;
using Appearance = WinLayer.Core.Appearance;

namespace WinLayer.Desktop;

internal static partial class CaptionTheme
{
    public static void Apply(nint window, Appearance appearance, AppTheme? theme = null)
    {
        var dark = theme is null ? IsDark(appearance) : ColorTranslator.FromHtml(theme.Canvas).GetBrightness() < 0.5f;
        if (SystemInformation.HighContrast)
        {
            Set(window, 20, 0);
            Set(window, 35, uint.MaxValue);
            Set(window, 36, uint.MaxValue);
            Set(window, 34, uint.MaxValue);
            return;
        }

        Set(window, 20, dark ? 1u : 0u);
        Set(window, 35, ColorRef(theme?.Surface, dark ? 0x311A16u : 0xF9EEEDu));
        Set(window, 36, ColorRef(theme?.Ink, dark ? 0xFEEFF1u : 0x341D19u));
        Set(window, 34, ColorRef(theme?.Border, dark ? 0x523731u : 0xEEE0DCu));
    }

    internal static bool IsDark(Appearance appearance) =>
        appearance == Appearance.Dark || appearance == Appearance.System && SystemPrefersDark();

    private static bool SystemPrefersDark()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is 0;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private static uint ColorRef(string? html, uint fallback)
    {
        if (html is null) return fallback;
        var color = ColorTranslator.FromHtml(html);
        return (uint)color.R | (uint)color.G << 8 | (uint)color.B << 16;
    }

    public static void ThemeControl(nint window, bool dark) =>
        SetWindowTheme(window, !SystemInformation.HighContrast && dark ? "DarkMode_Explorer" : "Explorer", null);

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetWindowTheme(nint window, string application, string? identifier);

    private static void Set(nint window, uint attribute, uint value) =>
        DwmSetWindowAttribute(window, attribute, in value, sizeof(uint));

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, uint attribute, in uint value, uint size);
}
