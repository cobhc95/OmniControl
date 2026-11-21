namespace OmniControl.Core.Native;

/// Windows' performance counters (pdh.dll): the GPU engines' load, the physical disks' idle time and the ACPI thermal zones (Resources sampler).
internal static class PdhNative
{
    public const uint Ok = 0, MoreData = 0x800007D2;
    public const uint FormatDouble = 0x00000200, FormatNoCap100 = 0x00008000;

    /// PDH_CSTATUS_VALID_DATA and PDH_CSTATUS_NEW_DATA: an item's value can be used.
    public const uint ValidData = 0, NewData = 1;

    /// One PDH_FMT_COUNTERVALUE_ITEM_W of PDH_FMT_DOUBLE: the instance name, then the status and the value.
    [StructLayout(LayoutKind.Sequential)]
    public struct Item
    {
        public IntPtr Name;
        public uint Status;
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")]
    public static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    public static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr items);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr query);
}
