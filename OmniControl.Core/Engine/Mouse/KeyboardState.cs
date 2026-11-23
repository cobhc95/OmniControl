namespace OmniControl.Core.Engine.Mouse;

/// Modifier keys, as a set.
[Flags]
public enum Modifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// Reads which modifier keys are held right now.
public static class KeyboardState
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkLeftWin = 0x5B, VkRightWin = 0x5C, VkEscape = 0x1B;

    public static Modifiers Held()
    {
        var held = Modifiers.None;
        if (IsDown(VkControl)) held |= Modifiers.Ctrl;
        if (IsDown(VkMenu)) held |= Modifiers.Alt;
        if (IsDown(VkShift)) held |= Modifiers.Shift;
        if (IsDown(VkLeftWin) || IsDown(VkRightWin)) held |= Modifiers.Win;
        return held;
    }

    /// True while Esc is held. Only read, never hooked or swallowed, so other apps keep their Esc.
    public static bool EscapeHeld() => IsDown(VkEscape);

    public static bool SwitchingWindows() => IsDown(VkMenu) && IsDown(0x09); // Alt+Tab

    private static bool IsDown(int vk) => GetAsyncKeyState(vk) < 0; // the high bit means "down"
}
