namespace OmniControl.Core.Native;

/// user32's device-interface notice: a window asks to be told when a device of one interface class (the monitors) arrives or leaves.
/// Without it Windows only broadcasts "something changed" (DBT_DEVNODES_CHANGED), never which kind.
internal static class DeviceNotifyNative
{
    /// GUID_DEVINTERFACE_MONITOR: every monitor Windows knows, whether or not it is on the desktop yet.
    public static readonly Guid MonitorInterface = new("E6F07B5F-EE97-4A90-B076-33F57BF4EAA7");

    /// DBT_DEVTYP_DEVICEINTERFACE: the notice's header says the device is an interface of a registered class.
    public const int DeviceInterfaceType = 5;

    private const uint NotifyWindowHandle = 0; // DEVICE_NOTIFY_WINDOW_HANDLE

    [StructLayout(LayoutKind.Sequential)]
    private struct BroadcastInterfaceFilter
    {
        public int Size, DeviceType, Reserved;
        public Guid ClassGuid;
        public short Name; // the variable-length name is not needed to register
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BroadcastHeader
    {
        public int Size, DeviceType, Reserved;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref BroadcastInterfaceFilter filter, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterDeviceNotification(IntPtr handle);

    /// Asks Windows to tell <paramref name="window"/> when a device of <paramref name="interfaceClass"/> arrives or leaves; zero when refused.
    public static IntPtr RegisterForInterface(IntPtr window, Guid interfaceClass)
    {
        var filter = new BroadcastInterfaceFilter { Size = Marshal.SizeOf<BroadcastInterfaceFilter>(), DeviceType = DeviceInterfaceType, ClassGuid = interfaceClass };
        return RegisterDeviceNotification(window, ref filter, NotifyWindowHandle);
    }

    /// The kind of device a WM_DEVICECHANGE arrival or removal is about (its header's device type); null when it carries none.
    public static int? DeviceTypeOf(IntPtr broadcast) =>
        broadcast == IntPtr.Zero ? null : Marshal.PtrToStructure<BroadcastHeader>(broadcast).DeviceType;
}
