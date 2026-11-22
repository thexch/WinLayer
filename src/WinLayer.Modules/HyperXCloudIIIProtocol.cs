namespace WinLayer.Modules;

public static class HyperXCloudIIIProtocol
{
    public const ushort VendorId = 0x03F0;
    public const ushort UsagePage = 0xFF13;
    public const ushort Usage = 0x0001;
    public const int ReportLength = 62;
    public const string DeviceName = "HyperX Cloud III Wireless";

    private const byte ReportId = 0x66;
    private const byte LinkRequest = 0x82;
    private const byte BatteryRequest = 0x89;

    public static ReadOnlySpan<ushort> ProductIds => [0x05B7, 0x0C9D];

    public static byte[] CreateLinkRequest(int length) => CreateRequest(LinkRequest, length);
    public static byte[] CreateBatteryRequest(int length) => CreateRequest(BatteryRequest, length);

    public static bool? ParseLink(ReadOnlySpan<byte> report)
    {
        if (report.Length < 3 || report[0] != ReportId || report[1] is not (LinkRequest or 0x0B))
            return null;

        return report[2] switch { 0 => false, 1 => true, _ => null };
    }

    public static int? ParseBattery(ReadOnlySpan<byte> report)
    {
        if (report.Length < 5 || report[0] != ReportId || report[1] is not (BatteryRequest or 0x0D))
            return null;

        return (report[2] != 0 || report[3] != 0) && report[4] <= 100 ? report[4] : null;
    }

    private static byte[] CreateRequest(byte command, int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, ReportLength);
        var report = new byte[length];
        report[0] = ReportId;
        report[1] = command;
        return report;
    }
}
