using static OmniControl.Core.Native.D3dkmtNative;

namespace OmniControl.Core.Engine.Resources;

/// One graphics adapter that renders (not Windows' software one): a stable <paramref name="Id"/> (its PCI place, else its name), its
/// <paramref name="Name"/> as Windows names it and its <paramref name="Luid"/> for this boot, as the GPU counters and the temperature ids write it.
public sealed record GraphicsAdapter(string Id, string Name, string Luid);

/// THE list of the PC's graphics adapters, from the graphics kernel (no administrator rights). A few milliseconds: called on a worker at
/// start-up and after Windows announces a device change.
public static unsafe class GraphicsAdapters
{
    public static IReadOnlyList<GraphicsAdapter> List()
    {
        var adapters = Each((handle, luid) =>
        {
            var address = stackalloc uint[3];
            var id = Query(handle, AdapterAddress, address, AddressSize) ? $"pci{address[0]}-{address[1]}-{address[2]}" : Safe(NameOf(handle));
            return new GraphicsAdapter(id, NameOf(handle), luid);
        });
        // Two adapters without a PCI place and of the same name are told apart by their order.
        return adapters.Select((a, i) => adapters.Take(i).Any(b => b.Id == a.Id) ? a with { Id = $"{a.Id}-{i}" } : a).ToList();
    }

    /// <paramref name="read"/> on each adapter that renders in hardware (its open handle and LUID), each closed after; the results that are not null.
    internal static List<T> Each<T>(Func<uint, string, T?> read) where T : class
    {
        var results = new List<T>();
        var list = new EnumAdapters2();
        if (D3DKMTEnumAdapters2(ref list) != 0 || list.Count == 0) return results;
        var adapters = new AdapterInfo[list.Count];
        fixed (AdapterInfo* first = adapters)
        {
            list.Adapters = first;
            if (D3DKMTEnumAdapters2(ref list) != 0) return results;
        }
        foreach (var adapter in adapters.AsSpan(0, (int)list.Count))
        {
            var handle = adapter.Adapter;
            try
            {
                uint type = 0;
                // A driver that cannot say its type is taken as hardware (WDDM before 1.2).
                if (Query(handle, AdapterType, &type, sizeof(uint)) && ((type & RenderSupported) == 0 || (type & SoftwareDevice) != 0)) continue;
                if (read(handle, $"{adapter.LuidHigh:x8}{adapter.LuidLow:x8}") is { } result) results.Add(result);
            }
            finally
            {
                D3DKMTCloseAdapter(ref handle);
            }
        }
        return results;
    }

    /// The adapter's name as Windows gives it ("AMD Radeon RX 9070"), or "Graphics card".
    internal static string NameOf(uint handle)
    {
        var registry = new char[RegistryInfoSize / 2];
        string name;
        fixed (char* text = registry)
            name = Query(handle, AdapterRegistryInfo, text, RegistryInfoSize) ? new string(text).Trim() : string.Empty;
        return name.Length > 0 ? name : "Graphics card";
    }

    internal static bool Query(uint adapter, int type, void* data, int size)
    {
        var query = new QueryAdapterInfo { Adapter = adapter, Type = type, Data = data, DataSize = (uint)size };
        return D3DKMTQueryAdapterInfo(ref query) == 0;
    }

    /// Letters, digits and '-' only, so the id sits in a state's or setting's id as it is.
    private static string Safe(string text) => new(text.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
}
