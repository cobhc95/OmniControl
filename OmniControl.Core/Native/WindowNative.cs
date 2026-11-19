namespace OmniControl.Core.Native;

/// user32 calls for OmniControl's own plain Win32 windows: the window class and the window, painting, the pointer's capture and shape.
/// Used by the touchpad watcher's message-only window and the screenshot overlay's picture surface.
internal static class WindowNative
{
    public delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassEx
    {
        public uint Size, Style;
        public IntPtr WndProc;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PaintStruct
    {
        public IntPtr Dc;
        public int Erase;
        public ScreenshotNative.Rect Paint;
        public int Restore, IncUpdate;
        public fixed byte Reserved[32];
    }

    public const uint WsChild = 0x40000000, WsVisible = 0x10000000, WsClipSiblings = 0x04000000;
    public const uint WmPaint = 0x000F, WmEraseBackground = 0x0014, WmSetCursor = 0x0020;
    public const uint WmMouseMove = 0x0200, WmLeftButtonDown = 0x0201, WmLeftButtonUp = 0x0202;
    public const int HitClient = 1;
    public const int IdcIBeam = 32513, IdcCross = 32515, IdcSizeNwse = 32642, IdcSizeNesw = 32643, IdcSizeWe = 32644, IdcSizeNs = 32645, IdcSizeAll = 32646;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WndClassEx wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(uint exStyle, string className, string? name, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hwnd, ref ScreenshotNative.Rect rect, bool erase);
    [DllImport("user32.dll")] public static extern bool UpdateWindow(IntPtr hwnd);

    [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr GetCapture();
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr instance, nint name);
    [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
}
