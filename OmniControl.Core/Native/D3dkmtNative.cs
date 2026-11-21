namespace OmniControl.Core.Native;

/// The graphics kernel's thunks in gdi32 (WDDM): the adapters and each one's name and temperature, the figure Task Manager shows (Resources sampler).
internal static unsafe class D3dkmtNative
{
    /// KMTQUERYADAPTERINFOTYPE values used here.
    public const int AdapterRegistryInfo = 8, AdapterPerfData = 62;

    /// D3DKMT_ADAPTERREGISTRYINFO: four WCHAR[MAX_PATH] strings, the adapter's name first.
    public const int RegistryInfoSize = 4 * 260 * 2;

    /// D3DKMT_ADAPTER_PERFDATA (WDDM 2.5): its size and the offset of Temperature, in tenths of a degree Celsius.
    public const int PerfDataSize = 64, TemperatureAt = 56;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct AdapterInfo
    {
        public uint Adapter;
        public uint LuidLow;
        public int LuidHigh;
        public uint Sources;
        public int PrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EnumAdapters2
    {
        public uint Count;
        public AdapterInfo* Adapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public void* Data;
        public uint DataSize;
    }

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTEnumAdapters2(ref EnumAdapters2 adapters);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo query);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTCloseAdapter(ref uint adapter);
}
