using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinLayer.Core;
using WinLayer.Modules;

namespace WinLayer.Agent;

internal sealed class HubMonitoring : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly Queue<HubSample> _history = new(180);
    private readonly Dictionary<(int Pid, long Started), TimeSpan> _previousCpu = [];
    private readonly Dictionary<TrayMetric, NotifyIcon> _icons = [];
    private readonly HashSet<TrayMetric> _alerted = [];
    private long _viewExpires;
    private Page _view;
    private HubOptions _network = HubOptions.Default(HubTool.Network);
    private HubOptions _system = HubOptions.Default(HubTool.System);
    private bool _collecting;
    private bool _disposed;
    private int _generation;
    private int _tick;
    private Task<Reading>? _pending;
    private PdhMetrics? _pdh;
    private Reading? _previous;
    private long _processTime;

    public HubMetrics? Status { get; private set; }

    public HubMonitoring() => _timer.Tick += async (_, _) => await SampleAsync();

    public void Configure(HubOptions network, HubOptions system)
    {
        if (_network == network && _system == system)
            return;
        var reset = _network.Enabled != network.Enabled || _network.Adapter != network.Adapter || _system.Enabled != system.Enabled;
        _network = network;
        _system = system;
        _generation++;
        _alerted.Clear();
        if (reset)
        {
            _previous = null;
            _history.Clear();
            Status = null;
            _previousCpu.Clear();
            _processTime = 0;
        }

        ClearIcons();
        UpdateSampling();
    }

    public void SetView(Page page)
    {
        if (_disposed)
            return;
        var changed = page != _view;
        if (changed && _view == Page.System)
            ClearProcessHistory();
        _view = page;
        _viewExpires = Environment.TickCount64 + 7000;
        if (changed || !_timer.Enabled && WantsSamples)
            UpdateSampling();
    }

    private bool Visible(Page page) => _view == page && Environment.TickCount64 < _viewExpires;
    private bool WantsSamples => _network.Enabled || _system.Enabled && (_system.TrayMetrics.Length > 0 || Visible(Page.System));

    private void UpdateSampling()
    {
        _timer.Stop();
        if (!WantsSamples)
        {
            _previous = null;
            DisposeCountersWhenIdle();
            return;
        }
        _timer.Interval = 1000 * (_network.Enabled && _system.Enabled ? Math.Min(_network.Seconds, _system.Seconds) :
            _network.Enabled ? _network.Seconds : _system.Seconds);
        _timer.Start();
        _ = SampleAsync();
    }

    private void ClearProcessHistory()
    {
        _processTime = 0;
        _previousCpu.Clear();
        if (Status is { } current)
            Status = current with { Processes = [] };
    }

    private void DisposeCountersWhenIdle()
    {
        if (_pending is null && _pdh is not null)
        {
            _pdh.Dispose();
            _pdh = null;
        }
    }

    private async Task SampleAsync()
    {
        if (_collecting || _disposed)
            return;
        if (!WantsSamples)
        {
            _timer.Stop();
            _previous = null;
            ClearProcessHistory();
            DisposeCountersWhenIdle();
            return;
        }
        var generation = _generation;
        var network = _network;
        var system = _system;
        var detailed = system.Enabled && Visible(Page.System);
        if (!detailed && _processTime != 0)
            ClearProcessHistory();
        var systemActive = system.Enabled && (detailed || system.TrayMetrics.Length > 0);
        var topology = _previous is null || detailed && _previous.Cores is null || _tick++ % 10 == 0;
        _collecting = true;
        try
        {
            var previous = _previous;
            _pending = Task.Run(() => Collect(systemActive, detailed, system.TrayMetrics.Contains(TrayMetric.Gpu), network.Adapter, topology, previous));
            var current = await _pending;
            if (_disposed || generation != _generation)
                return;
            double? down = null, up = null, cpu = null;
            if (previous is not null)
            {
                if (previous.Adapter == current.Adapter && current.Adapter is not null)
                    (down, up) = HubCalculations.NetworkRate(current.Rx, current.Tx, previous.Rx, previous.Tx,
                        Stopwatch.GetElapsedTime(previous.Timestamp, current.Timestamp).TotalSeconds);
                if (systemActive)
                    cpu = HubCalculations.Utilization(current.Idle, current.Total, previous.Idle, previous.Total);
            }
            var ram = current.TotalRam > 0 ? 100d * (current.TotalRam - current.FreeRam) / current.TotalRam : (double?)null;
            _previous = current;
            var sample = new HubSample(DateTimeOffset.UtcNow, cpu, ram, down, up, current.Gpu);
            _history.Enqueue(sample);
            var history = Math.Max(network.Enabled ? network.History : 0, system.Enabled ? system.History : 0);
            while (_history.Count > history)
                _history.Dequeue();
            Status = new(current.Adapters, current.Adapter, current.Rx, current.Tx, _history.ToArray(),
                current.TotalRam, current.FreeRam, current.Processor, Environment.ProcessorCount,
                current.Disks, Status?.Processes ?? [], current.GpuName, current.GpuMemory, current.GpuUsed,
                current.Cores, current.CpuMHz, current.Logical, current.DiskRead, current.DiskWrite);
            UpdateIcons(sample, network, system);
        }
        catch (Exception error)
        {
            WindowsSettings.Log("Collecte système.", error);
        }
        finally
        {
            _collecting = false;
            _pending = null;
            if ((!_system.Enabled || _disposed || !Visible(Page.System) && !_system.TrayMetrics.Contains(TrayMetric.Gpu)) && _pdh is not null)
            {
                _pdh.Dispose();
                _pdh = null;
            }
        }
    }

    private Reading Collect(bool system, bool detailed, bool trayGpu, string selected, bool topology, Reading? previous)
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .ToArray();
        var active = selected.Length > 0 ? interfaces.FirstOrDefault(adapter => adapter.Id == selected) :
            interfaces.Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .OrderBy(NetworkPriority).ThenBy(adapter => adapter.Id, StringComparer.Ordinal).FirstOrDefault();
        long rx = 0, tx = 0;
        string? activeId = null;
        if (active?.OperationalStatus == OperationalStatus.Up)
        {
            try
            {
                var statistics = active.GetIPStatistics();
                (rx, tx) = (statistics.BytesReceived, statistics.BytesSent);
                activeId = active.Id;
            }
            catch (NetworkInformationException) { }
        }

        var adapters = interfaces.Select(adapter => new NetworkAdapter(adapter.Id, adapter.Name, adapter.NetworkInterfaceType.ToString())).ToArray();
        var reading = new Reading(Stopwatch.GetTimestamp(), adapters, activeId, rx, tx);
        if (!system)
            return reading;
        var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var gotMemory = GlobalMemoryStatusEx(ref mem);
        GetSystemTimes(out var idle, out var kernel, out var user);
        var identity = topology && detailed ? HardwareIdentity() :
            (previous?.Processor, previous?.CpuMHz, previous?.Cores, previous?.GpuName, previous?.GpuMemory);
        double?[]? logical = null;
        double? diskRead = null, diskWrite = null, gpu = null;
        long? gpuUsed = null;
        if ((detailed || trayGpu) && (_pdh is null || _pdh.Detailed != detailed))
        {
            _pdh?.Dispose();
            _pdh = new(detailed);
        }
        if (detailed || trayGpu)
            _pdh?.Read(out logical, out diskRead, out diskWrite, out gpu, out gpuUsed);
        return reading with
        {
            Idle = idle, Total = kernel + user, Processor = identity.Item1, CpuMHz = identity.Item2, Cores = identity.Item3,
            TotalRam = gotMemory ? (long)mem.TotalPhysical : null, FreeRam = gotMemory ? (long)mem.AvailablePhysical : null,
            Disks = detailed && topology ? Drives() : previous?.Disks ?? [], Gpu = gpu, GpuUsed = gpuUsed,
            GpuName = identity.Item4, GpuMemory = identity.Item5, Logical = logical, DiskRead = diskRead, DiskWrite = diskWrite
        };
    }

    private static int NetworkPriority(NetworkInterface adapter)
    {
        try { return adapter.GetIPProperties().GatewayAddresses.Count > 0 ? 0 : 1; }
        catch (NetworkInformationException) { return 2; }
    }

    private static HubDisk[] Drives()
    {
        var readings = new List<HubDisk>();
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable))
            try
            {
                if (drive.IsReady)
                    readings.Add(new(drive.Name, drive.TotalSize, drive.TotalFreeSpace));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        return readings.ToArray();
    }

    private static (string?, int?, int?, string?, long?) HardwareIdentity()
    {
        var processor = HardwareValue(() =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString")?.ToString()?.Trim(), key?.GetValue("~MHz") as int?);
        });
        var adapters = HardwareValue(() =>
        {
            using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            var values = new List<(string Name, long? Memory)>();
            foreach (var item in display?.GetSubKeyNames() ?? [])
            {
                var info = HardwareValue(() =>
                {
                    using var key = display?.OpenSubKey(item);
                    if (key?.GetValue("DriverDesc") is not string name || key.GetValue("MatchingDeviceId") is not string)
                        return ((string?)null, (long?)null);
                    var memory = key.GetValue("HardwareInformation.qwMemorySize") switch
                    {
                        long value => value,
                        byte[] { Length: >= 8 } bytes => BitConverter.ToInt64(bytes),
                        _ => (long?)null
                    };
                    return (name, memory);
                });
                if (info.Item1 is { } name)
                    values.Add((name, info.Item2));
            }
            return values;
        }) ?? [];
        uint size = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref size);
        int? cores = null;
        if (size is > 0 and < 1_048_576)
        {
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetLogicalProcessorInformationEx(0, buffer, ref size))
                {
                    var count = 0;
                    for (var offset = 0; offset + 8 <= size;)
                    {
                        var length = Marshal.ReadInt32(buffer, offset + 4);
                        if (length < 8 || offset + length > size)
                            break;
                        count++;
                        offset += length;
                    }
                    if (count > 0) cores = count;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return (processor.Item1 ?? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), processor.Item2, cores,
            adapters.Count > 0 ? string.Join(" / ", adapters.Select(adapter => adapter.Name).Distinct()) : null,
            adapters.Count > 0 && adapters.All(adapter => adapter.Memory is > 0) ? adapters.Sum(adapter => adapter.Memory) : null);
    }

    private static T? HardwareValue<T>(Func<T> read)
    {
        try { return read(); }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return default;
        }
    }

    public async Task RefreshProcessesAsync()
    {
        if (Status is null || !_system.Enabled || _disposed)
            return;
        var now = Stopwatch.GetTimestamp();
        var elapsed = _processTime == 0 ? 0 : Stopwatch.GetElapsedTime(_processTime, now).TotalMilliseconds;
        var previousTotals = new Dictionary<(int Pid, long Started), TimeSpan>(_previousCpu);
        var (processes, totals) = await Task.Run(() => ReadProcesses(elapsed, previousTotals));
        if (Status is not { } current || !_system.Enabled || _disposed)
            return;
        _processTime = now;
        _previousCpu.Clear();
        foreach (var (key, value) in totals)
            _previousCpu.Add(key, value);
        Status = current with { Processes = processes };
    }

    private static (HubProcess[], Dictionary<(int Pid, long Started), TimeSpan>) ReadProcesses(double elapsed,
        Dictionary<(int Pid, long Started), TimeSpan> previousTotals)
    {
        var processes = new List<HubProcess>();
        var totals = new Dictionary<(int Pid, long Started), TimeSpan>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    var id = process.Id;
                    var started = TryGet(() => new DateTimeOffset(process.StartTime).ToUnixTimeMilliseconds()) ?? 0;
                    var time = TryGet(() => process.TotalProcessorTime);
                    var ram = TryGet(() => process.WorkingSet64);
                    string? path;
                    try { path = process.MainModule?.FileName; }
                    catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException) { path = null; }
                    double? cpu = null;
                    if (time is { } total && started > 0)
                    {
                        var key = (id, started);
                        if (elapsed > 0 && previousTotals.TryGetValue(key, out var previous))
                            cpu = Math.Clamp(100 * (total - previous).TotalMilliseconds / elapsed / Environment.ProcessorCount, 0, 100);
                        totals[key] = total;
                    }
                    processes.Add(new(id, name, started, ram, cpu, path is { Length: <= 1024 } ? path : null));
                }
                catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        var icons = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var ordered = processes.OrderByDescending(process => process.Ram).Take(500).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var process = ordered[index];
            if (process.Path is not { } path)
                continue;
            if (!icons.TryGetValue(path, out var icon) && icons.Count < 32)
                icons[path] = icon = ProcessIcon(path);
            if (icon is not null && index < 64)
                ordered[index] = process with { Icon = icon };
        }
        return (ordered, totals);
    }

    private static string? ProcessIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;
            using var original = icon.ToBitmap();
            using var small = new Bitmap(original, 16, 16);
            using var bytes = new MemoryStream();
            small.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
            return bytes.Length <= 2048 ? Convert.ToBase64String(bytes.ToArray()) : null;
        }
        catch (Exception error) when (error is Win32Exception or ArgumentException or IOException or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static T? TryGet<T>(Func<T> action) where T : struct
    {
        try { return action(); }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or NotSupportedException) { return null; }
    }

    public void EndProcess(int pid, long started)
    {
        if (!_system.Enabled || pid == Environment.ProcessId ||
            Status?.Processes.Any(process => process.Pid == pid && process.Started == started) != true)
            throw new InvalidOperationException("Actualisez la liste des processus.");
        using var current = Process.GetCurrentProcess();
        using var target = Process.GetProcessById(pid);
        if (new DateTimeOffset(target.StartTime).ToUnixTimeMilliseconds() != started || target.ProcessName is "WinLayer" or "WinLayer.Settings" ||
            target.SessionId != current.SessionId || !IsProcessCritical(target.Handle, out var critical) || critical)
            throw new InvalidOperationException("Ce processus n’est pas accessible ou a changé.");
        if (!TerminateProcess(target.Handle, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void UpdateIcons(HubSample sample, HubOptions network, HubOptions system)
    {
        var wanted = new Dictionary<TrayMetric, (string Text, Color Color)>();
        if (network.Enabled && network.Indicator && sample.Down is { } down && sample.Up is { } up)
            wanted[TrayMetric.Network] = ($"WinLayer · ↓ {Rate(down, network.RateUnit)}  ↑ {Rate(up, network.RateUnit)}", ColorTranslator.FromHtml(network.Color));
        if (system.Enabled)
        {
            foreach (var metric in system.TrayMetrics)
            {
                var value = metric switch { TrayMetric.Cpu => sample.Cpu, TrayMetric.Ram => sample.Ram, TrayMetric.Gpu => sample.Gpu, _ => null };
                if (value is not { } percent)
                    continue;
                var summary = $"WinLayer · {metric.ToString().ToUpperInvariant()} {percent:F0} %";
                var color = ColorTranslator.FromHtml(system.Color);
                wanted[metric] = (summary, color);
                if (system.Alerts && percent >= system.Threshold && _alerted.Add(metric))
                    SetIcon(metric, summary, color).ShowBalloonTip(3000, "Seuil système", summary, ToolTipIcon.Info);
                if (percent < system.Threshold)
                    _alerted.Remove(metric);
            }
        }
        foreach (var metric in _icons.Keys.Where(metric => !wanted.ContainsKey(metric)).ToArray())
            RemoveIcon(metric);
        foreach (var (metric, (text, color)) in wanted)
            SetIcon(metric, text, color);
    }

    private NotifyIcon SetIcon(TrayMetric metric, string text, Color color)
    {
        if (!_icons.TryGetValue(metric, out var icon))
        {
            using var bitmap = new Bitmap(16, 16);
            using var graphics = Graphics.FromImage(bitmap);
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 0, 0, 16, 16);
            TextRenderer.DrawText(graphics, metric == TrayMetric.Network ? "N" : metric.ToString()[..1],
                SystemFonts.DefaultFont, new Rectangle(0, 0, 16, 16),
                color.GetBrightness() < 0.5 ? Color.White : Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var handle = bitmap.GetHicon();
            using var borrowed = Icon.FromHandle(handle);
            icon = new NotifyIcon { Icon = (Icon)borrowed.Clone(), Visible = true };
            DestroyIcon(handle);
            _icons.Add(metric, icon);
        }
        icon.Text = text[..Math.Min(text.Length, 63)];
        return icon;
    }

    private static string Rate(double bytes, RateUnit unit) => unit switch
    {
        RateUnit.Megabytes => $"{bytes / 1_000_000:F2} MB/s",
        RateUnit.Megabits => $"{bytes / 125_000:F2} Mb/s",
        _ => $"{bytes / 1_000:F1} KB/s"
    };

    private void RemoveIcon(TrayMetric key)
    {
        var icon = _icons[key];
        var image = icon.Icon;
        icon.Dispose();
        image?.Dispose();
        _icons.Remove(key);
    }

    private void ClearIcons()
    {
        foreach (var key in _icons.Keys.ToArray())
            RemoveIcon(key);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
        ClearIcons();
        if (_pending is null)
        {
            _pdh?.Dispose();
            _pdh = null;
        }
    }

    private sealed record Reading(long Timestamp, NetworkAdapter[] Adapters, string? Adapter, long Rx, long Tx)
    {
        public ulong Idle, Total;
        public long? TotalRam, FreeRam, GpuMemory, GpuUsed;
        public int? Cores, CpuMHz;
        public string? Processor, GpuName;
        public double? Gpu, DiskRead, DiskWrite;
        public double?[]? Logical;
        public HubDisk[] Disks = [];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Percent;
        public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] private static extern bool IsProcessCritical(IntPtr process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
