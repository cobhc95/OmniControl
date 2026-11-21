using static OmniControl.Core.Native.PdhNative;

namespace OmniControl.Core.Engine.Resources;

/// One of Windows' performance counters with every instance (<c>\Set(*)\Counter</c>, by its English path so a translated Windows works too),
/// in a query of its own. A rate counter has values from its second <see cref="Read"/> on; an instance without a value now is left out.
internal sealed class PdhCounter : IDisposable
{
    private IntPtr _query, _counter;

    private PdhCounter(IntPtr query, IntPtr counter) => (_query, _counter) = (query, counter);

    /// The counter, or null when this Windows has no such counter set (no WDDM GPU counters, no ACPI thermal zone provider).
    public static PdhCounter? Open(string path)
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out var query) != Ok) return null;
        if (PdhAddEnglishCounter(query, path, IntPtr.Zero, out var counter) == Ok && PdhCollectQueryData(query) == Ok) return new PdhCounter(query, counter);
        PdhCloseQuery(query);
        return null;
    }

    /// Takes a new look and gives every instance's value; empty when Windows gave none now.
    public IReadOnlyList<(string Instance, double Value)> Read()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != Ok) return Array.Empty<(string, double)>();
        const uint format = FormatDouble | FormatNoCap100;
        uint size = 0;
        if (PdhGetFormattedCounterArray(_counter, format, ref size, out _, IntPtr.Zero) != MoreData || size == 0) return Array.Empty<(string, double)>();
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(_counter, format, ref size, out var count, buffer) != Ok) return Array.Empty<(string, double)>();
            var values = new List<(string, double)>((int)count);
            var stride = Marshal.SizeOf<Item>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<Item>(buffer + i * stride);
                if (item.Status is ValidData or NewData && Marshal.PtrToStringUni(item.Name) is { } name) values.Add((name, item.Value));
            }
            return values;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query == IntPtr.Zero) return;
        PdhCloseQuery(_query);
        (_query, _counter) = (IntPtr.Zero, IntPtr.Zero);
    }
}
