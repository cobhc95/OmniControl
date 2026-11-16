using System.Runtime.InteropServices;

namespace OmniControl.Core.Native;

/// <summary>user32 calls that bring one window of another program to the front.</summary>
internal static class ForegroundNative
{
    public const int SwRestore = 9;

    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
}
