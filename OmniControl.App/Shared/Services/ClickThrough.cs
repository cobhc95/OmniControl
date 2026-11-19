using System.Runtime.InteropServices;

namespace OmniControl.App.Shared.Services;

/// Makes a pop-up window ignore the mouse, stay out of Alt+Tab and never take focus. Safe to call again on an already styled window.
public static class ClickThrough
{
    private const int GwlExStyle = -20;
    private const long Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    public static void Apply(Window window)
    {
        if (window.TryGetPlatformHandle() is not { } handle) return;
        var style = GetWindowLongPtr(handle.Handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle.Handle, GwlExStyle, new IntPtr(style | Transparent | ToolWindow | NoActivate));
    }
}
