using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;

namespace WinLayer.Tests;

[TestClass]
public sealed class BatteryTrayDisplayTests
{
    [TestMethod]
    [DataRow("Casque Bluetooth", BatteryTrayGlyph.Headset)]
    [DataRow("Xbox Wireless Controller", BatteryTrayGlyph.Gamepad)]
    [DataRow("MX Master 3", BatteryTrayGlyph.Mouse)]
    [DataRow("Clavier de bureau", BatteryTrayGlyph.Keyboard)]
    [DataRow("Périphérique BLE", BatteryTrayGlyph.Battery)]
    public void PicksTheDeviceGlyphAndIncludesTheExactCharge(string name, BatteryTrayGlyph glyph)
    {
        var display = BatteryTrayDisplay.FromDevice(new("device", name, BatteryState.Available, 100));
        Assert.IsNotNull(display);
        Assert.AreEqual(glyph, display.Value.Glyph);
        Assert.AreEqual(100, display.Value.Percentage);
    }

    [TestMethod]
    public void RecognizesHyperXAndRepresentsZeroPercent()
    {
        var headset = new BatteryDevice("hyperx:test", "Cloud III", BatteryState.Available, 0, BatterySource.HyperXUsb);
        var before = BatteryTrayDisplay.FromDevice(headset)!.Value;
        Assert.AreEqual(BatteryTrayGlyph.Headset, before.Glyph);
        Assert.AreEqual(0, before.Percentage);
    }

    [TestMethod]
    public void OmitsDisconnectedAndInvalidReadings()
    {
        var device = new BatteryDevice("device", "Souris", BatteryState.Disconnected, 34);
        Assert.IsNull(BatteryTrayDisplay.FromDevice(device));
        Assert.IsNull(BatteryTrayDisplay.FromDevice(device with { State = BatteryState.Available, Percentage = null }));
        Assert.IsNull(BatteryTrayDisplay.FromDevice(device with { State = BatteryState.Available, Percentage = 101 }));
    }
}
