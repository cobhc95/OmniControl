using System.Runtime.InteropServices;

namespace OmniControl.Core.Native;

/// <summary>user32, gdi32 and winmm calls of the screen recorder: the pointer, the mouse buttons, the timer resolution and the recording bar's window.</summary>
internal static class VideoNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CursorInfo
    {
        public int Size, Flags;
        public IntPtr Cursor;
        public ScreenshotNative.Point Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        public bool IsIcon;
        public int HotspotX, HotspotY;
        public IntPtr Mask, Color;
    }

    public const int CursorShowing = 1, DiNormal = 3;
    public const int VkLeftButton = 1, VkRightButton = 2, VkMiddleButton = 4;
    public const uint WdaExcludeFromCapture = 0x11;
    public const int RgnOr = 2;

    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);

    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr target, IntPtr first, IntPtr second, int mode);
    [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
}
