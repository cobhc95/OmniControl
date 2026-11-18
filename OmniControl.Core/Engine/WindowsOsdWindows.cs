using System.Runtime.InteropServices;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Core.Engine;

/// <summary>
/// The Windows 11 pop-up windows that may hold Windows' own volume and brightness pop-up: Explorer windows of class
/// XamlExplorerHostIslandWindow (empty title) holding a DesktopWindowContentBridge "DesktopWindowXamlSource" with an InputSite child. Explorer
/// uses the same kind of window for other pop-ups too (the taskbar's thumbnail previews), so a window found here is only a candidate; which
/// one is the volume pop-up is decided by the caller (<c>WindowsOsd</c>). Every change here is asynchronous and never activates a window:
/// OmniControl never waits on Explorer and never moves the foreground (a plain minimise activates the next window, a plain restore activates
/// the restored one).
/// </summary>
public static class WindowsOsdWindows
{
    private const string HostClass = "XamlExplorerHostIslandWindow";
    private const int SwShowNoActivate = 4, SwShowMinNoActive = 7;
    private const ushort VkVolumeMute = 0xAD;

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    /// <summary>Every candidate window that exists now.</summary>
    public static List<IntPtr> Find()
    {
        var list = new List<IntPtr>();
        var host = IntPtr.Zero;
        while ((host = FindWindowEx(IntPtr.Zero, host, HostClass, string.Empty)) != IntPtr.Zero)
        {
            var bridge = FindWindowEx(host, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", "DesktopWindowXamlSource");
            if (bridge == IntPtr.Zero) continue;
            if (FindWindowEx(bridge, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null) == IntPtr.Zero) continue;
            list.Add(host);
        }
        return list;
    }

    /// <summary>True when the window is on screen: visible and not minimised.</summary>
    public static bool IsShown(IntPtr window) => IsWindowVisible(window) && !IsIconic(window);

    public static bool IsMinimised(IntPtr window) => IsIconic(window);

    /// <summary>Minimises the window without activating any other (SW_SHOWMINNOACTIVE), without waiting for Explorer.</summary>
    public static void Minimise(IntPtr window) => ShowWindowAsync(window, SwShowMinNoActive);

    /// <summary>Puts a minimised window back without activating it (SW_SHOWNOACTIVATE), without waiting for Explorer.</summary>
    public static void Restore(IntPtr window) => ShowWindowAsync(window, SwShowNoActivate);

    /// <summary>
    /// True when the pointer is over the taskbar or over one of these pop-ups: a pop-up that opens then is the taskbar's own (a thumbnail
    /// preview), never taken for the volume pop-up.
    /// </summary>
    public static bool PointerOverShell()
    {
        if (SystemCommands.GetCursorPos() is not { } at) return false;
        var className = WindowQuery.RootClassAt(at.X, at.Y);
        return PointerArea.IsTaskbar(className) || className == HostClass;
    }

    /// <summary>The window's handle and place for a log line ("0x1a2b at 100,200 300x80").</summary>
    public static string Describe(IntPtr window) =>
        GetWindowRect(window, out var r)
            ? $"0x{window.ToInt64():x} at {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}"
            : $"0x{window.ToInt64():x}";

    /// <summary>Mute pressed twice (the mute state ends as it was): makes Windows show, and if needed create, its volume pop-up.</summary>
    public static void ShowVolumePopup()
    {
        InputSender.Tap(VkVolumeMute, 0, false);
        InputSender.Tap(VkVolumeMute, 0, false);
    }
}
