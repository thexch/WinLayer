using System.Threading.Channels;
using WinLayer.Core;

namespace WinLayer.Modules;

public sealed record ModuleDescriptor(string Id, string DisplayName, IReadOnlyList<string> Capabilities, bool RequiresElevation);

public interface IWinLayerModule<in TOptions> : IAsyncDisposable
{
    ModuleDescriptor Descriptor { get; }
    // A disabled configuration stops and releases the module before returning.
    Task ApplyAsync(TOptions options);
}

public interface IBatteryProvider : IDisposable
{
    void Start(Action devicesChanged);
    Task<IReadOnlyList<BatteryDevice>> ReadAsync(CancellationToken cancellationToken);
}

// Calls to ApplyAsync and DisposeAsync are serialized by the host.
public sealed class DeviceBatteryModule(
    IReadOnlyList<Func<IBatteryProvider>> createProviders,
    Action<BatteryStatus> publish,
    Action<BatteryDevice> alert,
    Action<Exception> report) : IWinLayerModule<BatteryOptions>
{
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private Channel<byte>? _refresh;
    private BatteryOptions _options = new();
    private readonly LowBatteryAlerts _alerts = new();

    public ModuleDescriptor Descriptor { get; } = new("device-battery", "Device Battery",
        ["Lecture des périphériques Bluetooth LE associés", "Lecture du dongle HyperX Cloud III Wireless",
            "Notifications locales de batterie faible"],
        RequiresElevation: false);

    public async Task ApplyAsync(BatteryOptions options)
    {
        options = options.Normalize();
        if (_options == options)
            return;

        var wasEnabled = _options.Enabled;
        await StopAsync();
        _options = options;

        if (!options.Enabled)
        {
            _alerts.Clear();
            publish(new([]));
            return;
        }

        if (!wasEnabled)
            publish(new([]));

        var refresh = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        var stop = new CancellationTokenSource();
        _refresh = refresh;
        _stop = stop;
        _worker = Task.Run(() => MonitorAsync(options, refresh, stop.Token));
    }

    public bool RequestRefresh() => _refresh?.Writer.TryWrite(0) == true;

    private async Task MonitorAsync(BatteryOptions options, Channel<byte> refresh, CancellationToken stop)
    {
        var sources = createProviders.Select(factory => new BatterySourceSession(factory)).ToArray();
        Action changed = () => refresh.Writer.TryWrite(0);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var results = await Task.WhenAll(sources.Select(source => ReadSourceAsync(source, changed, stop)));
                stop.ThrowIfCancellationRequested();

                var devices = results.Where(result => result is not null).SelectMany(result => result!)
                    .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                var error = results.All(result => result is null) ? "Lecture des batteries indisponible." :
                    results.Any(result => result is null) ? "Une source de batterie est indisponible." : null;

                publish(new(devices, DateTimeOffset.Now, error));
                foreach (var device in _alerts.Observe(devices, options))
                    alert(device);

                using var interval = CancellationTokenSource.CreateLinkedTokenSource(stop);
                interval.CancelAfter(TimeSpan.FromMinutes(options.RefreshMinutes));
                try
                {
                    await refresh.Reader.ReadAsync(interval.Token);
                    // Coalesce connection and arrival events while Windows settles.
                    await Task.Delay(TimeSpan.FromSeconds(2), stop);
                    while (refresh.Reader.TryRead(out _))
                    {
                    }
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                {
                    // The configured fallback refresh is due.
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            report(error);
        }
        finally
        {
            foreach (var source in sources)
                Release(source.Provider);
        }
    }

    private async Task<IReadOnlyList<BatteryDevice>?> ReadSourceAsync(
        BatterySourceSession source, Action changed, CancellationToken stop)
    {
        try
        {
            if (source.Provider is null)
            {
                var provider = source.Create();
                try
                {
                    provider.Start(changed);
                    source.Provider = provider;
                }
                catch
                {
                    Release(provider);
                    throw;
                }
            }

            return await source.Provider.ReadAsync(stop);
        }
        catch (Exception error) when (!stop.IsCancellationRequested)
        {
            report(error);
            return null;
        }
    }

    private void Release(IBatteryProvider? provider)
    {
        try
        {
            provider?.Dispose();
        }
        catch (Exception error)
        {
            report(error);
        }
    }

    private sealed class BatterySourceSession(Func<IBatteryProvider> factory)
    {
        public IBatteryProvider? Provider { get; set; }
        public IBatteryProvider Create() => factory();
    }

    private async Task StopAsync()
    {
        if (_stop is null)
            return;

        var stop = _stop;
        _refresh = null;
        _stop = null;
        try
        {
            await stop.CancelAsync();
            await _worker!;
        }
        catch (Exception error)
        {
            report(error);
        }
        finally
        {
            _worker = null;
            stop.Dispose();
        }
    }

    public async ValueTask DisposeAsync() => await ApplyAsync(_options with { Enabled = false });
}

public sealed class LowBatteryAlerts
{
    private readonly HashSet<string> _notified = new(StringComparer.Ordinal);

    public IEnumerable<BatteryDevice> Observe(IReadOnlyList<BatteryDevice> devices, BatteryOptions options)
    {
        if (!options.Alerts)
        {
            Clear();
            yield break;
        }

        foreach (var device in devices)
        {
            if (device.State != BatteryState.Available || device.Percentage is not (>= 0 and <= 100))
                continue;

            if (device.Percentage > options.LowThreshold)
                _notified.Remove(device.Id);
            else if (_notified.Add(device.Id))
                yield return device;
        }
    }

    public void Clear() => _notified.Clear();
}
