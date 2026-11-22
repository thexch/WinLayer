using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Modules;

namespace WinLayer.Tests;

[TestClass]
public sealed class HyperXCloudIIIProtocolTests
{
    [TestMethod]
    public void RequestsCarryOnlyReadCommandsAndZeroPadding()
    {
        var link = HyperXCloudIIIProtocol.CreateLinkRequest(62);
        var battery = HyperXCloudIIIProtocol.CreateBatteryRequest(64);
        Assert.HasCount(62, link);
        Assert.HasCount(64, battery);
        CollectionAssert.AreEqual(new byte[] { 0x66, 0x82 }, link[..2]);
        CollectionAssert.AreEqual(new byte[] { 0x66, 0x89 }, battery[..2]);
        Assert.IsTrue(link.Skip(2).All(value => value == 0));
        Assert.IsTrue(battery.Skip(2).All(value => value == 0));
    }

    [TestMethod]
    [DataRow(0x89, 48)]
    [DataRow(0x0D, 0)]
    [DataRow(0x89, 100)]
    public void ReadsBatteryEchoOrResponseIncludingZeroAndFull(int command, int percentage)
    {
        Assert.AreEqual(percentage, HyperXCloudIIIProtocol.ParseBattery([0x66, (byte)command, 0x0E, 0xD7, (byte)percentage]));
    }

    [TestMethod]
    public void IgnoresMissingVoltageWrongReportAndInvalidPercentage()
    {
        Assert.IsNull(HyperXCloudIIIProtocol.ParseBattery([0x66, 0x89, 0, 0, 0]));
        Assert.IsNull(HyperXCloudIIIProtocol.ParseBattery([0x66, 0x89, 0x0E, 0xD7, 101]));
        Assert.IsNull(HyperXCloudIIIProtocol.ParseBattery([0x66, 0x82, 0x0E, 0xD7, 48]));
        Assert.IsNull(HyperXCloudIIIProtocol.ParseBattery([0x66, 0x89, 0x0E]));
    }

    [TestMethod]
    public void OnlyExplicitLinkStatusMarksHeadsetDisconnected()
    {
        Assert.AreEqual(true, HyperXCloudIIIProtocol.ParseLink([0x66, 0x82, 1]));
        Assert.AreEqual(false, HyperXCloudIIIProtocol.ParseLink([0x66, 0x0B, 0]));
        Assert.IsNull(HyperXCloudIIIProtocol.ParseLink([0x66, 0x82, 2]));
        Assert.IsNull(HyperXCloudIIIProtocol.ParseLink([0x66, 0x89, 0]));
    }
}
