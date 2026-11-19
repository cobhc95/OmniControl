using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Gestures;

/// Draws where the gesture areas are on the real screen (the "Show margins" switch): every screen-edge strip, taskbar
/// and the active window's title bar, each named, plus a pill giving the edge strip width. All areas are drawn whether or not their
/// gesture is on. What is drawn comes from <see cref="AreaMap"/>, which uses the gesture hit test's own geometry, order and
/// title-bar test with the same clamped edge strip the hook uses, so the drawing shows exactly where a gesture fires.
/// There is one window per monitor, covering it and cut down to the area rectangles and labels in exact screen pixels (a window of
/// the strip's own size would be made bigger by Windows' minimum window size); labels sit just inside the screen beside their area
/// and are pushed clear of every area and earlier label. While hidden there are no windows and no timer.
public sealed class MarginOverlay : IDisposable
{
    // Device-independent pixels between an area and its label, and between the pill and the bottom of the primary screen's working area.
    private const double LabelGap = 4, PillMargin = 24;

    private readonly GestureSettings _settings;
    private readonly AreaMap _map = new();
    private readonly List<MarginWindow> _windows = new();
    private DispatcherTimer? _timer;

    // A lookup is running (it may map a title bar, which is slow), and another was asked for meanwhile.
    private bool _busy, _again;

    public MarginOverlay(GestureSettings settings) => _settings = settings;

    public void Show()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = Timing.GestureMarginsRefresh };
        _timer.Tick += OnTick;
        _timer.Start();
        Refresh();
    }

    public void Hide()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
        foreach (var window in _windows) window.Close();
        _windows.Clear();
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    /// Looks the areas up again (off the UI thread) and redraws what changed. Does nothing while hidden.
    public async void Refresh()
    {
        if (_timer is null) return;
        if (_busy) { _again = true; return; }
        _busy = true;
        var strip = _settings.ClampedEdgeStrip();
        var areas = await Task.Run(() => _map.Areas(strip));
        _busy = false;
        if (_timer is not null) Draw(areas, strip);
        if (!_again) return;
        _again = false;
        Refresh();
    }

    private void Draw(IReadOnlyList<MappedArea> areas, int strip)
    {
        var accent = AccentService.Current;
        var screens = WindowAt(0).Screens;
        var taken = areas.SelectMany(a => a.Pieces).ToList(); // labels never cover an area or another label
        var labels = new List<MarginLabelItem>();

        foreach (var area in areas)
        {
            var centre = new PixelPoint(area.Bounds.Left + area.Bounds.Width / 2, area.Bounds.Top + area.Bounds.Height / 2);
            var screen = screens.ScreenFromPoint(centre);
            var scale = screen?.Scaling ?? 1;
            var outlined = area.Kind is PointerAreaKind.Taskbar or PointerAreaKind.TitleBar;
            var name = GestureCatalog.AreaName(area.Kind);
            var size = MarginLabel.Measure(name, accent, outlined, scale);
            var at = BesideArea(area, size, screen?.Bounds, (int)(LabelGap * scale), taken);
            labels.Add(new MarginLabelItem(at, name, outlined));
            taken.Add(at);
        }

        if (screens.Primary is { } primary)
        {
            var work = primary.WorkingArea;
            var scale = primary.Scaling;
            var text = $"{GestureSettings.EdgeStripLabel}: {strip}{GestureSettings.EdgeStripUnit}";
            var size = MarginLabel.Measure(text, accent, false, scale);
            var left = work.X + (work.Width - size.Width) / 2;
            var top = work.Bottom - size.Height - (int)(PillMargin * scale);
            var at = Clear(new ScreenRect(left, top, left + size.Width, top + size.Height), (0, -1), (int)(LabelGap * scale), taken);
            labels.Add(new MarginLabelItem(at, text, false));
        }

        // One window per monitor, showing the parts of the drawing that fall on it.
        var used = 0;
        foreach (var screen in screens.All)
        {
            var bounds = new ScreenRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Right, screen.Bounds.Bottom);
            var pieces = areas.SelectMany(a => a.Pieces.Select(p => new ScreenArea(a.Kind, p))).Where(p => p.Rect.Intersects(bounds)).ToList();
            var here = labels.Where(l => l.Rect.Intersects(bounds)).ToList();
            var window = WindowAt(used++);
            if (pieces.Count + here.Count == 0) window.HideScene();
            else window.ShowScene(new MarginScene(bounds, screen.Scaling, accent, pieces, here));
        }

        while (_windows.Count > used)
        {
            _windows[^1].Close();
            _windows.RemoveAt(_windows.Count - 1);
        }
    }

    private MarginWindow WindowAt(int index)
    {
        if (index == _windows.Count) _windows.Add(new MarginWindow());
        return _windows[index];
    }

    /// Where an area's label goes: just beside the area on the side facing into the screen (the side with the most room: above or below
    /// a wide area, left or right of a tall one), then pushed further that way past anything already <paramref name="taken"/>.
    /// Along a wide area it sits a quarter of the way (the edge and the taskbar) or in the middle (the title bar), so they do not meet.
    private static ScreenRect BesideArea(MappedArea area, PixelSize size, PixelRect? screen, int gap, IReadOnlyList<ScreenRect> taken)
    {
        var a = area.Bounds;
        var way = Inward(a, screen);
        int x, y;
        if (way.Dx != 0)
        {
            x = way.Dx > 0 ? a.Right + gap : a.Left - gap - size.Width;
            y = a.Top + (a.Height - size.Height) / 2;
        }
        else
        {
            var along = area.Kind == PointerAreaKind.TitleBar ? a.Width / 2 : a.Width / 4;
            x = a.Left + along - size.Width / 2;
            y = way.Dy > 0 ? a.Bottom + gap : a.Top - gap - size.Height;
        }
        return Clear(new ScreenRect(x, y, x + size.Width, y + size.Height), way, gap, taken);
    }

    /// The side of <paramref name="area"/> with the most room on its screen.
    private static (int Dx, int Dy) Inward(ScreenRect area, PixelRect? screen)
    {
        if (screen is not { } s) return (0, 1);
        return area.Width >= area.Height
            ? (0, s.Bottom - area.Bottom >= area.Top - s.Y ? 1 : -1)
            : (s.Right - area.Right >= area.Left - s.X ? 1 : -1, 0);
    }

    /// Moves <paramref name="rect"/> along <paramref name="way"/> until it overlaps nothing in <paramref name="taken"/>.
    private static ScreenRect Clear(ScreenRect rect, (int Dx, int Dy) way, int gap, IReadOnlyList<ScreenRect> taken)
    {
        // Each move goes past one taken rectangle and never back, so there are at most as many moves as rectangles.
        for (var moves = 0; moves <= taken.Count; moves++)
        {
            var blocker = taken.Where(t => t.Intersects(rect)).Cast<ScreenRect?>().FirstOrDefault();
            if (blocker is not { } b) return rect;
            var (dx, dy) = way.Dx > 0 ? (b.Right + gap - rect.Left, 0)
                : way.Dx < 0 ? (b.Left - gap - rect.Right, 0)
                : way.Dy > 0 ? (0, b.Bottom + gap - rect.Top)
                : (0, b.Top - gap - rect.Bottom);
            rect = new ScreenRect(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy);
        }
        return rect;
    }

    public void Dispose() => Hide();
}
