using static OmniControl.Core.Native.PdhNative;

namespace OmniControl.Core.Engine.Resources;

/// One of Windows' performance counters with every instance (<c>\Set(*)\Counter</c>, by its English path so a translated Windows works too),
/// in a query of its own. A rate counter has values from its second read on; an instance without a value now is left out.
/// Reuses one native buffer across reads and exposes instance names as <see cref="ReadOnlySpan{T}"/> to avoid per-tick allocations.
internal sealed unsafe class PdhCounter : IDisposable
{
    public delegate void SampleVisitor(ReadOnlySpan<char> instance, double value);

    private IntPtr _query, _counter, _buffer;
    private uint _bufferSize;

    private PdhCounter(IntPtr query, IntPtr counter) => (_query, _counter) = (query, counter);

    /// The counter, or null when this Windows has no such counter set (no WDDM GPU counters, no ACPI thermal zone provider).
    public static PdhCounter? Open(string path)
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out var query) != Ok) return null;
        if (PdhAddEnglishCounter(query, path, IntPtr.Zero, out var counter) == Ok && PdhCollectQueryData(query) == Ok) return new PdhCounter(query, counter);
        PdhCloseQuery(query);
        return null;
    }

    /// Takes a new look and passes each valid instance name and value to <paramref name="visit"/> without allocating managed strings or native buffers per call.
    public void ReadEach(SampleVisitor visit)
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != Ok) return;
        const uint format = FormatDouble | FormatNoCap100;
        var size = _bufferSize;
        uint count = 0;
        var status = _buffer != IntPtr.Zero
            ? PdhGetFormattedCounterArray(_counter, format, ref size, out count, _buffer)
            : MoreData;
        if (status == MoreData)
        {
            if (size == 0 && PdhGetFormattedCounterArray(_counter, format, ref size, out _, IntPtr.Zero) != MoreData) return;
            if (size == 0) return;
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal((int)size);
            _bufferSize = size;
            status = PdhGetFormattedCounterArray(_counter, format, ref size, out count, _buffer);
        }
        else if (status != Ok) return;
        if (status != Ok) return;

        var items = (Item*)_buffer;
        for (uint i = 0; i < count; i++)
        {
            var item = items[i];
            if (item.Status is ValidData or NewData && item.Name != IntPtr.Zero)
                visit(MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)item.Name), item.Value);
        }
    }

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            (_buffer, _bufferSize) = (IntPtr.Zero, 0);
        }
        if (_query == IntPtr.Zero) return;
        PdhCloseQuery(_query);
        (_query, _counter) = (IntPtr.Zero, IntPtr.Zero);
    }
}
