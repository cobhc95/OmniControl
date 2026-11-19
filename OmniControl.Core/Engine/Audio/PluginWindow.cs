namespace OmniControl.Core.Engine;

/// The Windows calls both plug-in editors share: moving a plug-in's own window inside the host window the app gives it.
internal static class PluginWindow
{
    private const uint GwChild = 5;
    private const uint SwpNoSize = 0x0001, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// Moves the plug-in's window (the first child of <paramref name="parent"/>) to <paramref name="x"/>, <paramref name="y"/> (pixels;
    /// zero or negative), so a part of an editor larger than its space can be scrolled into view. The parent window clips the rest.
    public static void MoveFirstChild(IntPtr parent, int x, int y)
    {
        if (parent == IntPtr.Zero) return;
        var view = GetWindow(parent, GwChild);
        if (view != IntPtr.Zero) SetWindowPos(view, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }
}
