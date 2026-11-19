using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Core.Engine.Trackpad;

/// The real <see cref="IShellSurfaceProbe"/>: the window in front, which of Windows' panels it is (its process and class, against
/// <see cref="ShellSurfaces.All"/>), and closing it with Esc, sent only while that same window is still in front.
public sealed class ShellSurfaceProbe : IShellSurfaceProbe
{
    private static readonly ushort[] EscapeKeys = KeyChord.TryParse("Esc", out var keys) ? keys : Array.Empty<ushort>();

    public IntPtr Foreground() => WindowQuery.ActiveWindow();

    public ShellSurface SurfaceOf(IntPtr window)
    {
        if (window == IntPtr.Zero || WindowQuery.ProcessOf(window) is not { } pid) return ShellSurface.None;
        try
        {
            using var process = Process.GetProcessById(pid);
            return ShellSurfaces.Of(process.ProcessName, WindowQuery.ClassOf(window));
        }
        // The process ended between the two calls, or its name can't be read: not a panel to close.
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return ShellSurface.None;
        }
    }

    public bool Dismiss(IntPtr window)
    {
        if (EscapeKeys.Length == 0 || WindowQuery.ActiveWindow() != window) return false;
        InputSender.Chord(EscapeKeys); // pressed and released in one call: nothing stays held
        return true;
    }
}
