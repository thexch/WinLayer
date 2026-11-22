using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Tests;

[TestClass]
public sealed class DeviceBatteryTests
{
    [TestMethod]
    public async Task DisabledModuleNeverCreatesAProvider()
    {
        var providersCreated = 0;
        await using var module = new DeviceBatteryModule(
            [() => { providersCreated++; return new WaitingProvider(); }],
            _ => { }, _ => { }, _ => Assert.Fail("Unexpected error."));

        await module.ApplyAsync(new() { Enabled = false, RefreshMinutes = 20 });

        Assert.AreEqual(0, providersCreated);
        Assert.IsFalse(module.RequestRefresh());
    }

    [TestMethod]
    public async Task DisablingCancelsTheReadAndDisposesTheProviderBeforeReactivation()
    {
        var first = new WaitingProvider();
        var second = new WaitingProvider();
        var calls = 0;
        await using var module = new DeviceBatteryModule([() => ++calls == 1 ? first : second],
            _ => { }, _ => { }, _ => Assert.Fail("Unexpected error."));

        await module.ApplyAsync(new() { Enabled = true });
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await module.ApplyAsync(new() { Enabled = false });
        Assert.IsTrue(first.WasDisposed);
        Assert.IsTrue(first.ReadCancelled);
        Assert.IsFalse(module.RequestRefresh());

        await module.ApplyAsync(new() { Enabled = true });
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task ProviderEventsAndManualRefreshCoalesceIntoOneRead()
    {
        var provider = new EventProvider();
        await using var module = new DeviceBatteryModule([() => provider], _ => { }, _ => { },
            _ => Assert.Fail("Unexpected error."));

        await module.ApplyAsync(new() { Enabled = true });
        await provider.FirstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        provider.RaiseChanged();
        provider.RaiseChanged();
        Assert.IsTrue(module.RequestRefresh());
        await provider.SecondRead.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.AreEqual(2, provider.Reads);
        await module.ApplyAsync(new() { Enabled = false });
        Assert.IsTrue(provider.WasDisposed);
    }

    [TestMethod]
    public void AlertsFireAtThresholdAndRearmOnlyAfterRecovery()
    {
        var alerts = new LowBatteryAlerts();
        var options = new BatteryOptions { LowThreshold = 20 };
        var device = new BatteryDevice("a", "Headset", BatteryState.Available, 20);

        CollectionAssert.AreEqual(new[] { device }, alerts.Observe([device], options).ToArray());
        Assert.IsEmpty(alerts.Observe([device with { Percentage = 19 }], options).ToArray());
        Assert.IsEmpty(alerts.Observe([device with { State = BatteryState.Disconnected, Percentage = null }], options).ToArray());
        Assert.IsEmpty(alerts.Observe([device], options).ToArray());
        Assert.IsEmpty(alerts.Observe([device with { Percentage = 21 }], options).ToArray());
        CollectionAssert.AreEqual(new[] { device }, alerts.Observe([device], options).ToArray());
    }

    [TestMethod]
    public void AlertsIgnoreUnknownInvalidAndDisabledReadings()
    {
        var alerts = new LowBatteryAlerts();
        var options = new BatteryOptions();
        var device = new BatteryDevice("a", "Mouse", BatteryState.Available, 5);

        Assert.IsEmpty(alerts.Observe(
            [device with { State = BatteryState.Unknown }, device with { Percentage = -1 }], options).ToArray());
        Assert.IsEmpty(alerts.Observe([device], options with { Alerts = false }).ToArray());
        CollectionAssert.AreEqual(new[] { device }, alerts.Observe([device], options).ToArray());
    }

    [TestMethod]
    public async Task FailingBluetoothSourcePreservesHyperXReading()
    {
        var device = new BatteryDevice("hyperx:receiver", HyperXCloudIIIProtocol.DeviceName, BatteryState.Available,
            12, BatterySource.HyperXUsb);
        var snapshot = new TaskCompletionSource<BatteryStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notification = new TaskCompletionSource<BatteryDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var module = new DeviceBatteryModule(
            [() => new FixedProvider(null), () => new FixedProvider(device)],
            status => { if (status.UpdatedAt.HasValue) snapshot.TrySetResult(status); },
            reading => notification.TrySetResult(reading),
            _ => { });

        await module.ApplyAsync(new() { Enabled = true });
        var result = await snapshot.Task.WaitAsync(deadline.Token);

        Assert.AreEqual(device, result.Devices.Single());
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(device, await notification.Task.WaitAsync(deadline.Token));
    }

    private sealed class FixedProvider(BatteryDevice? reading) : IBatteryProvider
    {
        public void Start(Action devicesChanged) { }
        public Task<IReadOnlyList<BatteryDevice>> ReadAsync(CancellationToken cancellationToken) =>
            reading is null ? throw new IOException("Test source unavailable.") :
            Task.FromResult<IReadOnlyList<BatteryDevice>>([reading]);
        public void Dispose() { }
    }

    private sealed class WaitingProvider : IBatteryProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasDisposed { get; private set; }
        public bool ReadCancelled { get; private set; }

        public void Start(Action devicesChanged) { }

        public async Task<IReadOnlyList<BatteryDevice>> ReadAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return [];
            }
            catch (OperationCanceledException)
            {
                ReadCancelled = true;
                throw;
            }
        }

        public void Dispose() => WasDisposed = true;
    }

    private sealed class EventProvider : IBatteryProvider
    {
        private Action? _changed;
        private int _reads;
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads => Volatile.Read(ref _reads);
        public bool WasDisposed { get; private set; }

        public void Start(Action devicesChanged) => _changed = devicesChanged;
        public void RaiseChanged() => _changed?.Invoke();

        public Task<IReadOnlyList<BatteryDevice>> ReadAsync(CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _reads);
            if (count == 1)
                FirstRead.TrySetResult();
            if (count == 2)
                SecondRead.TrySetResult();
            return Task.FromResult<IReadOnlyList<BatteryDevice>>([]);
        }

        public void Dispose()
        {
            _changed = null;
            WasDisposed = true;
        }
    }
}
