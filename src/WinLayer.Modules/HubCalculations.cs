using System.Globalization;
using WinLayer.Core;

namespace WinLayer.Modules;

public static class HubCalculations
{
    public static (double? Down, double? Up) NetworkRate(long received, long sent, long previousReceived,
        long previousSent, double seconds) => seconds > 0 && received >= previousReceived && sent >= previousSent
        ? ((received - previousReceived) / seconds, (sent - previousSent) / seconds) : (null, null);

    public static double? Utilization(ulong idle, ulong total, ulong previousIdle, ulong previousTotal)
    {
        if (total <= previousTotal || idle < previousIdle)
            return null;
        return Math.Clamp(100d * (1 - (idle - previousIdle) / (double)(total - previousTotal)), 0, 100);
    }

    public static string Measure(int x1, int y1, int x2, int y2, RulerMode mode)
    {
        var width = Math.Abs((long)x2 - x1);
        var height = Math.Abs((long)y2 - y1);
        return mode switch
        {
            RulerMode.Horizontal => $"{width} px",
            RulerMode.Vertical => $"{height} px",
            _ => FormattableString.Invariant($"{width} × {height} px · {Math.Sqrt((double)width * width + (double)height * height):0.##} px")
        };
    }

    public static string FormatColor(string hex, ColorFormat format)
    {
        if (!HubOptions.IsColor(hex))
            throw new ArgumentException("Invalid color.", nameof(hex));
        var red = byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var green = byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var blue = byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (format == ColorFormat.Hex)
            return hex.ToUpperInvariant();
        if (format == ColorFormat.Rgb)
            return $"rgb({red}, {green}, {blue})";

        var max = Math.Max(red, Math.Max(green, blue)) / 255d;
        var min = Math.Min(red, Math.Min(green, blue)) / 255d;
        var lightness = (max + min) / 2;
        var range = max - min;
        var saturation = range == 0 ? 0 : range / (1 - Math.Abs(2 * lightness - 1));
        var hue = range == 0 ? 0 : Math.Max(red, Math.Max(green, blue)) == red ? (green - blue) / (range * 255) :
            green >= blue ? 2 + (blue - red) / (range * 255) : 4 + (red - green) / (range * 255);
        return FormattableString.Invariant($"hsl({(hue * 60 + 360) % 360:0.#}, {saturation * 100:0.#}%, {lightness * 100:0.#}%)");
    }
}
