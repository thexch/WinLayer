using System.Security.Cryptography;
using System.Text;

namespace WinLayer.Core;

public enum BatteryTrayGlyph { Battery, Headset, Gamepad, Mouse, Keyboard }

public readonly record struct BatteryTrayDisplay(Guid IconId, BatteryTrayGlyph Glyph, int Percentage, string Tooltip)
{
    public static BatteryTrayDisplay? FromDevice(BatteryDevice device)
    {
        if (device.State != BatteryState.Available || device.Percentage is not (>= 0 and <= 100) ||
            string.IsNullOrWhiteSpace(device.Id))
            return null;

        var name = device.Name;
        var glyph = device.Source == BatterySource.HyperXUsb || Has(name, "headset", "headphone", "casque", "earbud", "airpods") ? BatteryTrayGlyph.Headset :
            Has(name, "controller", "gamepad", "manette", "dualsense", "dualshock", "xbox", "joy-con", "8bitdo") ? BatteryTrayGlyph.Gamepad :
            Has(name, "mouse", "souris", "mx master", "mx anywhere", "magic mouse") ? BatteryTrayGlyph.Mouse :
            Has(name, "keyboard", "clavier", "mx keys") ? BatteryTrayGlyph.Keyboard : BatteryTrayGlyph.Battery;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"WinLayer.Battery.{device.Id.ToUpperInvariant()}"));
        return new(new Guid(hash.AsSpan(0, 16)), glyph, device.Percentage.Value,
            $"{name} · {device.Percentage.Value} %");
    }

    private static bool Has(string name, params string[] terms) =>
        terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));
}
