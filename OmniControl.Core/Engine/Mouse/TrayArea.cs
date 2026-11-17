using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Mouse;

/// <summary>Finds where the taskbar's notification area is on screen (where the tray flyout opens when no icon was clicked).</summary>
public static class TrayArea
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    /// <summary>The centre of the notification area in screen pixels, or null when the taskbar has none (hidden or replaced shell).</summary>
    public static (int X, int Y)? Centre()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return null;
        var area = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (GetWindowRect(area == IntPtr.Zero ? taskbar : area, out var rect) is false) return null;
        return ((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
    }
}
