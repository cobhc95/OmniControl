using System.Runtime.InteropServices;

namespace OmniControl.Core.Native;

/// <summary>
/// user32's display configuration: which monitor (by model name) is on which display, and whether it is the laptop's own panel.
/// The structures are laid out exactly as in wingdi.h (a path is 72 bytes, a mode 64).
/// </summary>
internal static class DisplayConfigNative
{
    public const uint ActivePaths = 0x2;                       // QDC_ONLY_ACTIVE_PATHS
    private const uint SourceDeviceName = 1, TargetDeviceName = 2;
    private const uint OutputInternal = 0x80000000, OutputDisplayPortEmbedded = 11, OutputUdiEmbedded = 13;

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential, Size = 72)]
    public struct PathInfo
    {
        public Luid SourceAdapter;
        public uint SourceId;
        public uint SourceModeIndex;
        public uint SourceStatus;
        public Luid TargetAdapter;
        public uint TargetId;
        public uint TargetModeIndex;
        public uint OutputTechnology;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    public struct ModeInfo { public uint InfoType; public uint Id; public Luid Adapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Header { public uint Type; public uint Size; public Luid Adapter; public uint Id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public Header Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetName
    {
        public Header Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufacturer;
        public ushort EdidProduct;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] pathArray, ref uint modes, [Out] ModeInfo[] modeArray, IntPtr topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SourceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TargetName request);

    /// <summary>What Windows knows about the monitor on one display.</summary>
    public readonly record struct Target(string FriendlyName, string DevicePath, bool IsInternal);

    /// <summary>The monitor on each active display, by the display's device name. Empty when Windows cannot say.</summary>
    public static Dictionary<string, Target> ReadTargets()
    {
        var result = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(ActivePaths, out var pathCount, out var modeCount) != 0) return result;
        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(ActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;

        for (var i = 0; i < pathCount; i++)
        {
            var source = new SourceName { Header = new Header { Type = SourceDeviceName, Size = (uint)Marshal.SizeOf<SourceName>(), Adapter = paths[i].SourceAdapter, Id = paths[i].SourceId } };
            var target = new TargetName { Header = new Header { Type = TargetDeviceName, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = paths[i].TargetAdapter, Id = paths[i].TargetId } };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || DisplayConfigGetDeviceInfo(ref target) != 0) continue;

            var tech = paths[i].OutputTechnology;
            result[source.GdiDeviceName] = new Target(target.FriendlyName ?? string.Empty, target.DevicePath ?? string.Empty,
                tech is OutputInternal or OutputDisplayPortEmbedded or OutputUdiEmbedded);
        }
        return result;
    }
}
