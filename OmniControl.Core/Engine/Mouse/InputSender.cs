using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Mouse;

/// <summary>Sends keyboard and mouse input to Windows (SendInput). Everything sent carries <see cref="Marker"/> so <see cref="MouseHook"/> ignores it.</summary>
public static class InputSender
{
    /// <summary>Tag on every input this class sends ("OMNI"), so the mouse hook never remaps its own output.</summary>
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

    private static uint Send(params Input[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());

    private static Input Mouse(uint flags, uint data = 0) =>
        new() { Type = InputMouse, U = new Union { Mouse = new MouseData { Flags = flags, Data = data, Extra = Marker } } };

    private static Input Key(ushort vk, bool down) =>
        new()
        {
            Type = InputKeyboard,
            U = new Union { Key = new KeyData { Vk = vk, Flags = (down ? 0u : KeyUp) | (KeyChord.IsExtended(vk) ? KeyExtended : 0u), Extra = Marker } },
        };

    private static Input RawKey(ushort vk, ushort scan, uint flags) =>
        new() { Type = InputKeyboard, U = new Union { Key = new KeyData { Vk = vk, Scan = scan, Flags = flags, Extra = Marker } } };

    /// <summary>
    /// Presses and releases one key exactly as a keyboard reported it (virtual key and scan code, extended or not) in one call, so it is never
    /// left down. Returns true when Windows accepted both halves; if it took only the press, the release is sent at once.
    /// </summary>
    public static bool Tap(ushort vk, ushort scan, bool extended)
    {
        var flags = extended ? KeyExtended : 0u;
        var up = RawKey(vk, scan, flags | KeyUp);
        var sent = Send(RawKey(vk, scan, flags), up);
        if (sent == 1) Send(up);
        return sent == 2;
    }

    /// <summary>Presses all keys in order, then releases them in reverse (a shortcut such as Ctrl+C).</summary>
    public static void Chord(IReadOnlyList<ushort> keys) =>
        Send(keys.Select(k => Key(k, true)).Concat(keys.Reverse().Select(k => Key(k, false))).ToArray());

    /// <summary>Presses the keys and keeps them down (until <see cref="Release"/>), for actions that last while a mouse button is held.</summary>
    public static void Press(IReadOnlyList<ushort> keys) => Send(keys.Select(k => Key(k, true)).ToArray());

    /// <summary>Lets go of keys held by <see cref="Press"/>, in reverse order.</summary>
    public static void Release(IReadOnlyList<ushort> keys) => Send(keys.Reverse().Select(k => Key(k, false)).ToArray());

    private static (uint Down, uint Up, uint Data) FlagsOf(MouseButton button) => button switch
    {
        MouseButton.Right => (RightDown, RightUp, 0u),
        MouseButton.Middle => (MiddleDown, MiddleUp, 0u),
        MouseButton.Back => (XDown, XUp, 1u),
        MouseButton.Forward => (XDown, XUp, 2u),
        _ => (LeftDown, LeftUp, 0u),
    };

    /// <summary>A full click of a button (press and release), once or twice for a double click.</summary>
    public static void Click(MouseButton button, bool twice = false)
    {
        var (down, up, data) = FlagsOf(button);
        var pair = new[] { Mouse(down, data), Mouse(up, data) };
        Send(twice ? pair.Concat(pair).ToArray() : pair);
    }

    /// <summary>Presses a button and keeps it down (until <see cref="ButtonUp"/>), for click-drag.</summary>
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

    /// <summary>One wheel notch. Positive is up (or right when <paramref name="horizontal"/>). Returns how many inputs Windows accepted (0 means it refused).</summary>
    public static uint Scroll(int notches, bool horizontal = false) =>
        Send(Mouse(horizontal ? HWheel : Wheel, unchecked((uint)(notches * WheelStep))));
}
