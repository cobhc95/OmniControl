using static OmniControl.Core.Engine.Trackpad.RawInputNative;

namespace OmniControl.Core.Engine.Mouse;

/// Observes mouse presses for a visible window without capturing input or taking the foreground from Explorer.
public static unsafe class OutsidePress
{
    private const ushort MouseUsage = 2, LeftDown = 1, ButtonDown = LeftDown | 4 | 16 | 64 | 256;
    private const uint Owner = 4;

    [DllImport("user32.dll")] private static extern uint GetMessagePos();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint relation);

    public static void Listen(nint window, bool enabled)
    {
        var device = new RawInputDevice { UsagePage = GenericDesktopPage, Usage = MouseUsage,
            Flags = enabled ? RidevInputSink : RidevRemove, Target = enabled ? window : 0 };
        if (!RegisterRawInputDevices([device], 1, (uint)sizeof(RawInputDevice))) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct MouseData { [FieldOffset(4)] public ushort Buttons; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Packet { public RawInputHeader Header; public MouseData Mouse; }

    public static (int X, int Y, bool Left)? FromMessage(nint window, uint message, nint input)
    {
        if (message != WmInput) return null;
        Packet packet;
        uint size = (uint)sizeof(Packet);
        if (GetRawInputData(input, RidInput, &packet, ref size, (uint)HeaderSize) != size || packet.Header.Type != 0
            || (packet.Mouse.Buttons & ButtonDown) == 0) return null;
        var pos = GetMessagePos();
        int x = unchecked((short)pos), y = unchecked((short)(pos >> 16));
        var target = WindowQuery.RootWindowAt(x, y);
        if (target == 0) return null;
        for (; target != 0; target = GetWindow(target, Owner))
            if (target == window) return null; // includes pop-ups and nested picker windows
        return (x, y, (packet.Mouse.Buttons & LeftDown) != 0);
    }
}
