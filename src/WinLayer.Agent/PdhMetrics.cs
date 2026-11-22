using System.Runtime.InteropServices;

namespace WinLayer.Agent;

internal sealed class PdhMetrics : IDisposable
{
    private readonly IntPtr _query;
    private readonly IntPtr[] _cpu;
    private readonly IntPtr _read, _write, _gpu, _memory;

    public bool Detailed { get; }

    public PdhMetrics(bool detailed)
    {
        Detailed = detailed;
        _cpu = [];
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0)
            return;
        if (detailed)
        {
            _cpu = Enumerable.Range(0, Math.Min(Environment.ProcessorCount, 128))
            .Select(index => Add($@"\Processor({index})\% Processor Time")).ToArray();
        _read = Add(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
        _write = Add(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
        }
        _gpu = Add(@"\GPU Engine(*)\Utilization Percentage");
        _memory = Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
    }

    private IntPtr Add(string path) => PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var counter) == 0 ? counter : IntPtr.Zero;

    public void Read(out double?[]? logicalCpu, out double? read, out double? write, out double? gpu, out long? memory)
    {
        logicalCpu = [];
        read = write = gpu = null;
        memory = null;
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
            return;
        logicalCpu = _cpu.Select(Value).ToArray();
        read = Value(_read);
        write = Value(_write);
        var engines = ArrayValues(_gpu);
        if (engines.Count > 0)
            gpu = Math.Clamp(engines.GroupBy(entry =>
            {
                var index = entry.Name.IndexOf("luid_", StringComparison.Ordinal);
                return index >= 0 ? entry.Name[index..] : entry.Name;
            }).Max(group => group.Sum(entry => entry.Value)), 0, 100);
        var memoryValues = ArrayValues(_memory);
        if (memoryValues.Count > 0)
            memory = (long)memoryValues.Sum(entry => entry.Value);
    }

    private static double? Value(IntPtr counter) => counter != IntPtr.Zero &&
        PdhGetFormattedCounterValue(counter, 0x200, IntPtr.Zero, out var value) == 0 && Valid(value)
        ? value.Value : null;

    private static bool Valid(CounterValue value) => value.Status <= 1 && double.IsFinite(value.Value) && value.Value >= 0;

    private static List<(string Name, double Value)> ArrayValues(IntPtr counter)
    {
        var data = new List<(string, double)>();
        if (counter == IntPtr.Zero)
            return data;
        uint bytes = 0, count = 0;
        var result = PdhGetFormattedCounterArrayW(counter, 0x200, ref bytes, ref count, IntPtr.Zero);
        if (result != 0x800007D2 || bytes is 0 or > 4_194_304)
            return data;
        var address = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, 0x200, ref bytes, ref count, address) != 0)
                return data;
            var size = Marshal.SizeOf<CounterItem>();
            for (var index = 0; index < count && (index + 1) * size <= bytes; index++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(address + index * size);
                if (Valid(item.Counter) && Marshal.PtrToStringUni(item.Name) is { } name)
                    data.Add((name, item.Counter.Value));
            }
            return data;
        }
        finally { Marshal.FreeHGlobal(address); }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem { public IntPtr Name; public CounterValue Counter; }
    [StructLayout(LayoutKind.Explicit)]
    private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out CounterValue value);
    [DllImport("pdh.dll", ExactSpelling = true)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, ref uint count, IntPtr buffer);
}
