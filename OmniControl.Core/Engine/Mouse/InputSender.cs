namespace OmniControl.Core.Engine.Mouse;

/// Sends keyboard and mouse input to Windows (SendInput). Everything sent carries <see cref="Marker"/> so <see cref="MouseHook"/> ignores it.
public static class InputSender
{
    /// Tag on every input this class sends ("OMNI"), so the mouse hook never remaps its own output.
    internal static readonly UIntPtr Marker = (UIntPtr)0x4F4D4E49;

    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint KeyUp = 0x2, KeyExtended = 0x1;
    private const uint LeftDown = 0x2, LeftUp = 0x4, RightDown = 0x8, RightUp = 0x10, MiddleDown = 0x20, MiddleUp = 0x40;
    private const uint XDown = 0x80, XUp = 0x100, Wheel = 0x800, HWheel = 0x1000;
    private const int WheelStep = 120;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData { public int Dx, Dy; public uint Data, Flags, Time; public UIntPtr Extra; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyData { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra; }

    [StructLayout(LayoutKind.Explicit)]
    private struct Union { [FieldOffset(0)] public MouseData Mouse; [FieldOffset(0)] public KeyData Key; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public Union U; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);

    private const uint VkToScanEx = 4; // MAPVK_VK_TO_VSC_EX: the high byte is 0xE0 for an extended key
    private const uint ExtendedPrefix = 0xE0;

    private static uint Send(params Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());

    private static Input Mouse(uint flags, uint data = 0) =>
        new() { Type = InputMouse, U = new Union { Mouse = new MouseData { Flags = flags, Data = data, Extra = Marker } } };

    // The virtual key and its real scan code: Remote Desktop, virtual machine consoles and raw-input games pass on the scan code only,
    // so a key sent with scan code 0 (a virtual key alone) never reaches them.
    private static Input Key(ushort vk, bool down, IntPtr layout)
    {
        var scan = MapVirtualKeyEx(vk, VkToScanEx, layout);
        var extended = scan >> 8 == ExtendedPrefix || KeyChord.IsExtended(vk);
        return RawKey(vk, (ushort)(scan & 0xFF), (down ? 0u : KeyUp) | (extended ? KeyExtended : 0u));
    }

    private static Input RawKey(ushort vk, ushort scan, uint flags) =>
        new() { Type = InputKeyboard, U = new Union { Key = new KeyData { Vk = vk, Scan = scan, Flags = flags, Extra = Marker } } };

    /// Presses and releases one key exactly as a keyboard reported it (virtual key and scan code, extended or not) in one call, so it is never
    /// left down. Returns true when Windows accepted both halves; if it took only the press, the release is sent at once.
    public static bool Tap(ushort vk, ushort scan, bool extended)
    {
        var flags = extended ? KeyExtended : 0u;
        var up = RawKey(vk, scan, flags | KeyUp);
        var sent = Send(RawKey(vk, scan, flags), up);
        if (sent == 1) Send(up);
        return sent == 2;
    }

    /// Presses all keys in order, then releases them in reverse (a shortcut such as Ctrl+C).
    public static void Chord(IReadOnlyList<ushort> keys)
    {
        var layout = WindowQuery.ActiveKeyboardLayout();
        Send(keys.Select(k => Key(k, true, layout)).Concat(keys.Reverse().Select(k => Key(k, false, layout))).ToArray());
    }

    /// Presses the keys and keeps them down, for actions that last while a mouse button is held. Returns what lets go of exactly those keys
    /// in reverse order, with the scan codes they were pressed with even if the layout in front changed meanwhile.
    public static Action Press(IReadOnlyList<ushort> keys)
    {
        var layout = WindowQuery.ActiveKeyboardLayout();
        Send(keys.Select(k => Key(k, true, layout)).ToArray());
        var release = keys.Reverse().Select(k => Key(k, false, layout)).ToArray();
        return () => Send(release);
    }

    private static (uint Down, uint Up, uint Data) FlagsOf(MouseButton button) => button switch
    {
        MouseButton.Right => (RightDown, RightUp, 0u),
        MouseButton.Middle => (MiddleDown, MiddleUp, 0u),
        MouseButton.Back => (XDown, XUp, 1u),
        MouseButton.Forward => (XDown, XUp, 2u),
        _ => (LeftDown, LeftUp, 0u),
    };

    /// A full click of a button (press and release), once or twice for a double click.
    public static void Click(MouseButton button, bool twice = false)
    {
        var (down, up, data) = FlagsOf(button);
        var pair = new[] { Mouse(down, data), Mouse(up, data) };
        Send(twice ? pair.Concat(pair).ToArray() : pair);
    }

    /// Presses a button and keeps it down (until <see cref="ButtonUp"/>), for click-drag.
    public static void ButtonDown(MouseButton button)
    {
        var (down, _, data) = FlagsOf(button);
        Send(Mouse(down, data));
    }

    public static void ButtonUp(MouseButton button)
    {
        var (_, up, data) = FlagsOf(button);
        Send(Mouse(up, data));
    }

    /// One wheel notch. Positive is up (or right when <paramref name="horizontal"/>). Returns how many inputs Windows accepted (0 means it refused).
    public static uint Scroll(int notches, bool horizontal = false) =>
        Send(Mouse(horizontal ? HWheel : Wheel, unchecked((uint)(notches * WheelStep))));
}
