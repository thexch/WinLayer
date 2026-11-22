using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Agent;

internal sealed class WindowsBluetoothProvider : IBatteryProvider
{
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private static readonly string[] Properties = [ConnectedProperty];
    private static readonly string PairedSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
    private DeviceWatcher? _watcher;
    private Action? _devicesChanged;

    public void Start(Action devicesChanged)
    {
        _devicesChanged = devicesChanged;
        _watcher = DeviceInformation.CreateWatcher(PairedSelector, Properties, DeviceInformationKind.AssociationEndpoint);
        _watcher.Added += Added;
        _watcher.Updated += Updated;
        _watcher.Removed += Removed;
        _watcher.Start();
    }

    public async Task<IReadOnlyList<BatteryDevice>> ReadAsync(CancellationToken cancellationToken)
    {
        if (_watcher?.Status is DeviceWatcherStatus.Aborted or DeviceWatcherStatus.Stopped)
            _watcher.Start();

        using var discoveryTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        discoveryTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        var devices = await DeviceInformation.FindAllAsync(PairedSelector, Properties, DeviceInformationKind.AssociationEndpoint)
            .AsTask(discoveryTimeout.Token);
        var readings = new List<BatteryDevice>(devices.Count);
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            readings.Add(await ReadDeviceAsync(device, cancellationToken));
        }

        return readings.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static async Task<BatteryDevice> ReadDeviceAsync(DeviceInformation device, CancellationToken stop)
    {
        var unknown = new BatteryDevice(device.Id, string.IsNullOrWhiteSpace(device.Name) ? "Périphérique Bluetooth LE" : device.Name,
            BatteryState.Unknown);
        if (!device.Properties.TryGetValue(ConnectedProperty, out var connection) || connection is not bool connected)
            return unknown;

        if (!connected)
            return unknown with { State = BatteryState.Disconnected };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));

        try
        {
            using var bluetooth = await BluetoothLEDevice.FromIdAsync(device.Id).AsTask(timeout.Token);
            if (bluetooth is null)
                return unknown;
            if (bluetooth.ConnectionStatus != BluetoothConnectionStatus.Connected)
                return unknown with { State = BatteryState.Disconnected };

            var services = await bluetooth.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached)
                .AsTask(timeout.Token);
            if (services.Status != GattCommunicationStatus.Success)
                return unknown;

            try
            {
                foreach (var service in services.Services)
                {
                    var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel,
                        BluetoothCacheMode.Cached).AsTask(timeout.Token);
                    if (characteristics.Status != GattCommunicationStatus.Success)
                        continue;

                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        if (!characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
                            continue;

                        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                        if (result.Status != GattCommunicationStatus.Success || result.Value?.Length != 1)
                            continue;

                        using var value = DataReader.FromBuffer(result.Value);
                        var percentage = value.ReadByte();
                        if (percentage <= 100)
                            return unknown with { State = BatteryState.Available, Percentage = percentage };
                    }
                }

                return services.Services.Count == 0 ? unknown with { State = BatteryState.Unsupported } : unknown;
            }
            finally
            {
                foreach (var service in services.Services)
                    service.Dispose();
            }
        }
        catch (Exception error) when (!stop.IsCancellationRequested &&
            error is OperationCanceledException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return unknown;
        }
    }

    private void Added(DeviceWatcher watcher, DeviceInformation device)
    {
        if (watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
            _devicesChanged?.Invoke();
    }

    private void Updated(DeviceWatcher watcher, DeviceInformationUpdate update)
    {
        if (update.Properties.ContainsKey(ConnectedProperty))
            _devicesChanged?.Invoke();
    }

    private void Removed(DeviceWatcher watcher, DeviceInformationUpdate update) => _devicesChanged?.Invoke();

    public void Dispose()
    {
        _devicesChanged = null;
        if (_watcher is null)
            return;

        _watcher.Added -= Added;
        _watcher.Updated -= Updated;
        _watcher.Removed -= Removed;
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            _watcher.Stop();
        _watcher = null;
    }
}
