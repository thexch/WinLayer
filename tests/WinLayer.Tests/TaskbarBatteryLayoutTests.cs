using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;

namespace WinLayer.Tests;

[TestClass]
public sealed class TaskbarBatteryLayoutTests
{
    [TestMethod]
    public void PlacesTheStripImmediatelyLeftOfTheNotificationArea()
    {
        var taskbar = new Rectangle(0, 1032, 1920, 48);
        var tray = new Rectangle(1600, 1032, 320, 48);

        var band = TaskbarBatteryLayout.FindBand(taskbar, tray, 5);

        Assert.AreEqual(1595, band.Right);
        Assert.AreEqual(1032, band.Top);
        Assert.AreEqual(640, band.Width);
        Assert.AreEqual(48, band.Height);
    }

    [TestMethod]
    public void KeepsNegativeScreenOffsetsAndUsesTheDpiScaledGap()
    {
        var taskbar = new Rectangle(-1920, -300, 1920, 64);
        var tray = new Rectangle(-400, -300, 400, 64);

        Assert.AreEqual(new Rectangle(-1048, -300, 640, 64), TaskbarBatteryLayout.FindBand(taskbar, tray, 8));
    }

    [TestMethod]
    public void RejectsMissingOrVerticalTaskbarsAndMissingSpace()
    {
        Assert.IsTrue(TaskbarBatteryLayout.FindBand(new(0, 0, 48, 1080), new(0, 860, 48, 220), 5).IsEmpty);
        Assert.IsTrue(TaskbarBatteryLayout.FindBand(new(0, 1032, 1920, 48), Rectangle.Empty, 5).IsEmpty);
        Assert.IsTrue(TaskbarBatteryLayout.FindBand(new(0, 1032, 1920, 48), new(3, 1032, 320, 48), 5).IsEmpty);
    }
}
