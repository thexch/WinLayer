using System.Runtime.InteropServices;
using Microsoft.Win32;
using Appearance = WinLayer.Core.Appearance;

namespace WinLayer.Settings;

internal static partial class CaptionTheme
{
    public static void Apply(nint window, Appearance appearance)
    {
        var dark = appearance == Appearance.Dark || appearance == Appearance.System && SystemPrefersDark();
        if (SystemInformation.HighContrast)
        {
            Set(window, 20, 0);
            Set(window, 35, uint.MaxValue);
            Set(window, 36, uint.MaxValue);
            Set(window, 34, uint.MaxValue);
            return;
        }

        Set(window, 20, dark ? 1u : 0u);
        Set(window, 35, dark ? ColorRef(0x16, 0x1A, 0x31) : ColorRef(0xED, 0xEE, 0xF9));
        Set(window, 36, dark ? ColorRef(0xF1, 0xEF, 0xFE) : ColorRef(0x19, 0x1D, 0x34));
        Set(window, 34, dark ? ColorRef(0x31, 0x37, 0x52) : ColorRef(0xDC, 0xE0, 0xEE));
    }

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

    private static uint ColorRef(uint red, uint green, uint blue) => red | (green << 8) | (blue << 16);

    private static void Set(nint window, uint attribute, uint value) =>
        DwmSetWindowAttribute(window, attribute, in value, sizeof(uint));

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, uint attribute, in uint value, uint size);
}
