using System.Runtime.InteropServices;
using WinLayer.Core;

namespace WinLayer.Agent;

internal static partial class TrayBatteryIcon
{
    // Bitmap glyphs stay crisp at the taskbar's 16 px size.
    private static readonly string[] Digits =
    [
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001001010010", "111101111101111", "111101111001111"
    ];

    public static Icon Create(BatteryTrayDisplay display, int iconSize)
    {
        var scale = Math.Clamp(iconSize, 16, 64) / 16f;
        using var bitmap = new Bitmap((int)(scale * 16), (int)(scale * 16));
        using var graphics = Graphics.FromImage(bitmap);
        graphics.ScaleTransform(scale, scale);
        using var fill = new SolidBrush(display.Percentage <= 15 ? Color.FromArgb(255, 226, 183) : Color.FromArgb(224, 228, 255));
        using var ink = new SolidBrush(Color.FromArgb(32, 25, 69));
        graphics.FillRectangle(fill, 1, 0, 14, 16);
        graphics.FillRectangle(fill, 0, 1, 16, 14);

        var symbol = display.Glyph switch
        {
            BatteryTrayGlyph.Headset => "0111110" + "1000001" + "1000001" + "1100011" + "1100011",
            BatteryTrayGlyph.Gamepad => "0111110" + "1000001" + "1110101" + "1010001" + "1000001" + "0110110",
            BatteryTrayGlyph.Mouse => "0011100" + "0101010" + "0101010" + "0100010" + "0100010" + "0011100",
            BatteryTrayGlyph.Keyboard => "0000000" + "1111111" + "1010101" + "1000001" + "1111111",
            _ => "1111110" + "1000010" + "1011011" + "1000010" + "1111110"
        };
        Draw(graphics, ink, 4, 1, 7, symbol);

        var text = display.Percentage.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var x = (16 - text.Length * 4) / 2 - 1;
        foreach (var digit in text)
        {
            Draw(graphics, ink, x, 9, 3, Digits[digit - '0']);
            x += 4;
        }
        Draw(graphics, ink, x, 9, 3, "101001010100101");

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void Draw(Graphics graphics, Brush ink, int x, int y, int width, string pixels)
    {
        for (var i = 0; i < pixels.Length; i++)
            if (pixels[i] == '1')
                graphics.FillRectangle(ink, x + i % width, y + i / width, 1, 1);
    }

    [LibraryImport("user32.dll")]
    private static partial int DestroyIcon(nint icon);
}
