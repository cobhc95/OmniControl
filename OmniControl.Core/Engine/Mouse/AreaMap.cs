namespace OmniControl.Core.Engine.Mouse;

/// Where the gesture areas are on the screen right now, for drawing them, built from the same pieces the hit test
/// (<see cref="PointerArea.At"/>) uses: the edge strips of every monitor (<see cref="PointerArea.Edges"/>), every taskbar window, and
/// the active window's title bar mapped with the hit test's own question (<see cref="CaptionArea"/>). Where areas overlap, each is cut
/// by the ones that win over it in <see cref="PointerArea.At"/>'s order (taskbar, then edges, then title bar), so what is drawn is
/// exactly where each area applies. Keeps the last title-bar map, so use one instance from one thread at a time.
public sealed class AreaMap
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, ref Rect bounds, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    // The title bar last mapped, and for which window at which place and size: mapping it again is slow, so only on a change.
    private (IntPtr Window, ScreenRect Frame) _captionOf;
    private List<ScreenRect> _caption = new();

    /// <param name="edgeStrip">How many pixels deep the strip along a screen edge is (the value the gesture hook uses).</param>
    public IReadOnlyList<MappedArea> Areas(int edgeStrip)
    {
        var areas = new List<MappedArea>();
        var winners = new List<ScreenRect>(); // everything added so far wins over what comes after

        void Add(PointerAreaKind kind, IReadOnlyList<ScreenRect> parts)
        {
            var pieces = parts.SelectMany(p => Cut(p, winners)).ToList();
            winners.AddRange(parts);
            if (pieces.Count > 0) areas.Add(new MappedArea(kind, Bounds(parts), pieces));
        }

        foreach (var taskbar in PointerArea.Taskbars().Where(t => IsWindowVisible(t.Window))) Add(PointerAreaKind.Taskbar, new[] { taskbar.Rect });
        foreach (var edge in Monitors().SelectMany(m => PointerArea.Edges(m, edgeStrip))) Add(edge.Kind, new[] { edge.Rect });
        var caption = ActiveCaption();
        if (caption.Count > 0) Add(PointerAreaKind.TitleBar, caption);
        return areas;
    }

    /// What is left of <paramref name="rect"/> once every rectangle in <paramref name="cuts"/> is taken out.
    private static IEnumerable<ScreenRect> Cut(ScreenRect rect, IEnumerable<ScreenRect> cuts) =>
        cuts.Aggregate((IEnumerable<ScreenRect>)new[] { rect }, (left, cut) => left.SelectMany(r => r.Minus(cut)).ToList());

    private static ScreenRect Bounds(IReadOnlyList<ScreenRect> parts) =>
        new(parts.Min(p => p.Left), parts.Min(p => p.Top), parts.Max(p => p.Right), parts.Max(p => p.Bottom));

    private static ScreenRect ToScreen(Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private static List<ScreenRect> Monitors()
    {
        var monitors = new List<ScreenRect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref Rect bounds, IntPtr _) =>
        {
            monitors.Add(ToScreen(bounds));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    /// The active window's title bar, mapped again only when another window is active or it moved or changed size.
    private List<ScreenRect> ActiveCaption()
    {
        var window = WindowQuery.ActiveWindow();
        if (window == IntPtr.Zero || IsIconic(window) || !GetWindowRect(window, out var rect)) return new List<ScreenRect>();
        var className = WindowQuery.ClassOf(window);
        if (PointerArea.IsTaskbar(className) || PointerArea.IsDesktopClass(className)) return new List<ScreenRect>();

        var key = (window, ToScreen(rect));
        if (key != _captionOf)
        {
            _caption = CaptionArea.Of(window, key.Item2);
            _captionOf = key;
        }
        return _caption;
    }
}
