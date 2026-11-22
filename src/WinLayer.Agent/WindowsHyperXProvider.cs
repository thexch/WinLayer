using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using WinLayer.Core;
using WinLayer.Modules;
using Protocol = WinLayer.Modules.HyperXCloudIIIProtocol;

namespace WinLayer.Agent;

internal sealed partial class WindowsHyperXProvider : IBatteryProvider
{
    private const uint ReadWrite = 0xC0000000;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;
    private const uint Overlapped = 0x40000000;
    private const int InvalidFunctionHResult = unchecked((int)0x80070001);
    private const int HidSuccess = 0x00110000;
    private const string ContainerIdProperty = "System.Devices.ContainerId";
    private static readonly string[] Properties = [ContainerIdProperty];
    private static readonly string Selector = string.Join(" OR ", Protocol.ProductIds.ToArray().Select(productId =>
        $"({HidDevice.GetDeviceSelector(Protocol.UsagePage, Protocol.Usage, Protocol.VendorId, productId)})"));

    private DeviceWatcher? _watcher;
    private Action? _devicesChanged;

    public void Start(Action devicesChanged)
    {
        _devicesChanged = devicesChanged;
        _watcher = DeviceInformation.CreateWatcher(Selector, Properties, DeviceInformationKind.DeviceInterface);
        _watcher.Added += Added;
        _watcher.Removed += Removed;
        _watcher.Start();
    }

    public async Task<IReadOnlyList<BatteryDevice>> ReadAsync(CancellationToken stop)
    {
        if (_watcher?.Status is DeviceWatcherStatus.Aborted or DeviceWatcherStatus.Stopped)
            _watcher.Start();

        using var discovery = CancellationTokenSource.CreateLinkedTokenSource(stop);
        discovery.CancelAfter(TimeSpan.FromSeconds(5));
        var interfaces = await DeviceInformation.FindAllAsync(Selector, Properties, DeviceInformationKind.DeviceInterface)
            .AsTask(discovery.Token);

        var devices = new Dictionary<string, BatteryDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in interfaces)
        {
            stop.ThrowIfCancellationRequested();
            var id = GetId(endpoint);
            if (devices.TryGetValue(id, out var existing) && existing.State == BatteryState.Available)
                continue;

            var reading = await ReadEndpointAsync(endpoint.Id, id, stop);
            if (existing is null || reading.State != BatteryState.Unknown)
                devices[id] = reading;
        }

        return devices.Values.ToArray();
    }

    private static string GetId(DeviceInformation endpoint)
    {
        return endpoint.Properties.TryGetValue(ContainerIdProperty, out var container) && container is Guid id && id != Guid.Empty
            ? $"hyperx:{id}"
            : $"hyperx:{endpoint.Id}";
    }

    private static async Task<BatteryDevice> ReadEndpointAsync(string path, string id, CancellationToken stop)
    {
        var unknown = new BatteryDevice(id, Protocol.DeviceName, BatteryState.Unknown, Source: BatterySource.HyperXUsb);
        using var handle = CreateFile(path, ReadWrite, ShareReadWrite, 0, OpenExisting, Overlapped, 0);
        if (handle.IsInvalid || !GetCaps(handle, out var caps))
            return unknown;

        try
        {
            using var stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 1, isAsync: true);
            var input = new byte[caps.InputLength];
            var requestLength = IsReportLength(caps.OutputLength) ? caps.OutputLength : caps.FeatureLength;
            var link = await QueryAsync(stream, handle, caps, Protocol.CreateLinkRequest(requestLength), input,
                Protocol.ParseLink, stop);
            if (link == false)
                return unknown with { State = BatteryState.Disconnected };

            var level = await QueryAsync(stream, handle, caps, Protocol.CreateBatteryRequest(requestLength), input,
                Protocol.ParseBattery, stop);
            return level is null ? unknown : unknown with { State = BatteryState.Available, Percentage = level };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return unknown;
        }
    }

    private delegate T? ReadReport<T>(ReadOnlySpan<byte> report) where T : struct;

    private static async Task<T?> QueryAsync<T>(FileStream stream, SafeFileHandle handle, HidCaps caps,
        byte[] request, byte[] input, ReadReport<T> parse, CancellationToken stop) where T : struct
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            var useFeature = !IsReportLength(caps.OutputLength);
            if (!useFeature)
            {
                try
                {
                    await stream.WriteAsync(request, timeout.Token);
                }
                catch (IOException error) when (error.HResult == InvalidFunctionHResult)
                {
                    useFeature = true;
                }
            }

            if (useFeature)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (!IsReportLength(caps.FeatureLength))
                    return null;

                var feature = new byte[caps.FeatureLength];
                request.AsSpan(0, Protocol.ReportLength).CopyTo(feature);
                if (!SetFeature(handle, feature))
                    return null;
            }

            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var count = await stream.ReadAsync(input, timeout.Token);
                if (count == 0)
                    return null;
                if (parse(input.AsSpan(0, count)) is { } value)
                    return value;
            }
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool GetCaps(SafeFileHandle handle, out HidCaps caps)
    {
        caps = default;
        if (HidD_GetPreparsedData(handle, out var data) == 0)
            return false;

        try
        {
            return HidP_GetCaps(data, out caps) == HidSuccess &&
                caps.UsagePage == Protocol.UsagePage && caps.Usage == Protocol.Usage &&
                IsReportLength(caps.InputLength) && (IsReportLength(caps.OutputLength) || IsReportLength(caps.FeatureLength));
        }
        finally
        {
            HidD_FreePreparsedData(data);
        }
    }

    private static bool IsReportLength(ushort length) => length is >= Protocol.ReportLength and <= 64;

    private static unsafe bool SetFeature(SafeFileHandle handle, byte[] report)
    {
        fixed (byte* buffer = report)
            return HidD_SetFeature(handle, buffer, (uint)report.Length) != 0;
    }

    private void Added(DeviceWatcher watcher, DeviceInformation device)
    {
        if (watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
            _devicesChanged?.Invoke();
    }

    private void Removed(DeviceWatcher watcher, DeviceInformationUpdate device) => _devicesChanged?.Invoke();

    public void Dispose()
    {
        _devicesChanged = null;
        if (_watcher is not { } watcher)
            return;

        watcher.Added -= Added;
        watcher.Removed -= Removed;
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            watcher.Stop();
        _watcher = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct HidCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputLength;
        public ushort OutputLength;
        public ushort FeatureLength;
        // Reserved[17], then ten capability counters unused by the transport.
        private fixed ushort _remaining[27];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("hid.dll")]
    private static partial byte HidD_GetPreparsedData(SafeFileHandle handle, out nint data);

    [LibraryImport("hid.dll")]
    private static partial byte HidD_FreePreparsedData(nint data);

    [LibraryImport("hid.dll")]
    private static partial int HidP_GetCaps(nint data, out HidCaps caps);

    [LibraryImport("hid.dll")]
    private static unsafe partial byte HidD_SetFeature(SafeFileHandle handle, byte* report, uint length);
}
