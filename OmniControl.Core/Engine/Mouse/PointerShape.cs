using OmniControl.Core.Engine.Pointer;

namespace OmniControl.Core.Engine.Mouse;

/// Shows a chosen system pointer everywhere while a mouse mode runs, then puts the user's own pointers back. Windows has no
/// per-moment pointer for other programs' windows, so the pointers a program usually shows under a still mouse are replaced
/// (SetSystemCursor). Putting back is one call that reloads the user's cursor scheme from their settings, so there is no saved
/// copy that could be lost; <see cref="Reset"/> is also safe to call at any time (start-up and exit do), which clears a pointer
/// left behind by a crash.
public static class PointerShape
{
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool SetSystemCursor(IntPtr cursor, uint id);

    /// The system pointers a shape can be (Windows' own ids): four arrows, up/down, left/right, hand, crosshair.
    public enum Shape : uint { FourArrows = 32646, UpDown = 32645, LeftRight = 32644, Hand = 32649, Crosshair = 32515 }

    // The pointers a program usually shows under a still mouse: arrow, text beam, link hand, busy arrow.
    private static readonly uint[] Replaced = { 32512, 32513, 32649, 32650 };

    /// Shows <paramref name="shape"/> in place of the usual pointers. False when Windows refused (the pointers are reset then).
    public static bool Show(Shape shape)
    {
        var source = LoadCursor(IntPtr.Zero, (IntPtr)(uint)shape);
        if (source == IntPtr.Zero) return false;

        var ok = true;
        foreach (var id in Replaced)
        {
            // SetSystemCursor takes ownership of the handle it is given, so each pointer gets its own copy.
            var copy = CopyIcon(source);
            if (copy == IntPtr.Zero || !SetSystemCursor(copy, id))
            {
                if (copy != IntPtr.Zero) SpiNative.DestroyCursor(copy);
                ok = false;
            }
        }
        if (!ok) Reset();
        return ok;
    }

    /// True when the pointer showing now is Windows' hand (what browsers and most programs show over a link).
    public static bool IsHandShown()
    {
        var info = new Native.VideoNative.CursorInfo { Size = Marshal.SizeOf<Native.VideoNative.CursorInfo>() };
        return Native.VideoNative.GetCursorInfo(ref info) && info.Cursor != IntPtr.Zero && info.Cursor == LoadCursor(IntPtr.Zero, (IntPtr)(uint)Shape.Hand);
    }

    /// Puts the user's own pointers back by reloading their cursor scheme.
    public static void Reset() => SpiNative.Set(SpiNative.SetCursors, 0, IntPtr.Zero, 0);
}
