using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Trackpad;

/// <summary>Raw Input (user32), the HID report parser (hid.dll) and the message loop of the touchpad watcher's message-only window (the window itself: <see cref="OmniControl.Core.Native.WindowNative"/>).</summary>
internal static unsafe class RawInputNative
{
    public const ushort DigitizerPage = 0x0D, GenericDesktopPage = 0x01;
    public const ushort TouchpadUsage = 0x05, ContactCountUsage = 0x54, ContactIdUsage = 0x51, TipSwitchUsage = 0x42;
    public const ushort XUsage = 0x30, YUsage = 0x31;

    public const uint WmInput = 0x00FF, WmInputDeviceChange = 0x00FE, WmClose = 0x0010, WmDestroy = 0x0002;
    public const uint RidevRemove = 0x00000001, RidevInputSink = 0x00000100, RidevDevNotify = 0x00002000;
    public const uint RidInput = 0x10000003, RidiPreparsedData = 0x20000005, RidiDeviceInfo = 0x2000000b;
    public const uint RimTypeHid = 2;
    public const int HidpInput = 0;
    public const int HidpStatusSuccess = 0x00110000;
    public static readonly IntPtr HwndMessage = new(-3);

    /// <summary>RAWINPUTHEADER (24 bytes on 64-bit) then RAWHID's two counts: the HID reports start this far into the buffer.</summary>
    public static readonly int HeaderSize = sizeof(RawInputHeader);
    public static readonly int ReportsOffset = HeaderSize + 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputHeader { public uint Type, Size; public IntPtr Device, WParam; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDeviceListItem { public IntPtr Device; public uint Type; }

    /// <summary>RID_DEVICE_INFO with its HID part (the union is 24 bytes, the keyboard's).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct DeviceInfo
    {
        [FieldOffset(0)] public uint Size;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(20)] public ushort UsagePage;
        [FieldOffset(22)] public ushort Usage;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct HidpCaps
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(48)] public ushort NumberInputValueCaps;
    }

    /// <summary>HIDP_VALUE_CAPS (72 bytes); only the fields the touchpad needs. <see cref="Usage"/> is UsageMin when <see cref="IsRange"/>.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HidpValueCaps
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(56)] public ushort Usage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputData(IntPtr rawInput, uint command, void* data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetRawInputDeviceList([Out] RawInputDeviceListItem[]? list, ref uint count, uint size);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetRawInputDeviceInfoW")]
    public static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, void* data, ref uint size);

    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr preparsed, out HidpCaps caps);

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(int reportType, [Out] HidpValueCaps[] caps, ref ushort length, IntPtr preparsed);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint value,
        IntPtr preparsed, byte* report, uint reportLength);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, ushort* usages, ref uint length,
        IntPtr preparsed, byte* report, uint reportLength);

    [DllImport("user32.dll")] public static extern int GetMessage(out Msg m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref Msg m);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
}
