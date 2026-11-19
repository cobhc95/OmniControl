using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Platform;

namespace OmniControl.App.Features.Osd;

/// The OSD shown for as long as the placement window is open: the same visual as the real pop-up
/// at the place being edited, with a dashed accent edge, on the chosen screen. It can be dragged (mouse first: it snaps to the screen's
/// edges, the margin lines and the centre lines) and nudged with the arrow keys (1 px, Shift 10 px) once it has the keyboard. Every
/// spot it is moved to is reported through the callback; the placement maths are <see cref="OsdPlacement"/>, the same as the real pop-up's.
public sealed class OsdPreview : IDisposable
{
    private readonly PreviewWindow _window;
    private readonly Action<OsdSpot> _moved;
    private Screen? _screen;
    private OsdSpot _spot;

    public OsdPreview(OsdSettings settings, Action<OsdSpot> moved)
    {
        _moved = moved;
        _window = new PreviewWindow(settings);
        _window.Dragged += OnDragged;
        _window.Nudged += OnNudged;
    }

    /// The place the preview shows now.
    public OsdSpot Spot => _spot;

    /// Shows the preview at <paramref name="spot"/> on <paramref name="screen"/> (opens it the first time).
    public void Show(Screen screen, OsdSpot spot)
    {
        (_screen, _spot) = (screen, spot);
        Place();
        if (!_window.IsVisible) _window.Show();
    }

    /// Pins the preview to another anchor without moving it; returns the new offsets.
    public OsdSpot Repin(OsdAnchor anchor)
    {
        if (_screen is { } screen) _spot = OsdPlacement.Repin(_spot, anchor, screen.WorkingArea, screen.Scaling, SizeOn(screen));
        return _spot;
    }

    private PixelSize SizeOn(Screen screen) => OsdPlacement.SizeOf(_window.Look, screen.Scaling);

    private void Place()
    {
        if (_screen is not { } screen) return;
        _window.Position = OsdPlacement.Position(_spot, screen.WorkingArea, screen.Scaling, SizeOn(screen));
    }

    private void OnDragged(PixelPoint wanted)
    {
        if (_screen is not { } screen) return;
        var size = SizeOn(screen);
        var at = OsdPlacement.Snap(wanted, screen.WorkingArea, screen.Scaling, size);
        _spot = OsdPlacement.SpotAt(_spot.Anchor, at, screen.WorkingArea, screen.Scaling, size);
        Place();
        _moved(_spot);
    }

    private void OnNudged(int right, int down)
    {
        _spot = OsdPlacement.Nudge(_spot, right, down);
        Place();
        _moved(_spot);
    }

    public void Dispose()
    {
        _window.Dragged -= OnDragged;
        _window.Nudged -= OnNudged;
        _window.Close();
    }

    /// Layout and input only: the pop-up's visual under a dashed edge, a drag with the mouse and the arrow keys.
    private sealed class PreviewWindow : Window
    {
        private PixelPoint? _grab;

        public PreviewWindow(OsdSettings settings)
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Opacity = OsdSettings.ClampOpacity(settings.Opacity) / (double)Percent.Max;
            Cursor = new Cursor(StandardCursorType.SizeAll);
            Look = OsdView.Build(settings, OsdService.BrightnessElement, OsdLevelIcon.Sun, OsdView.SampleValue);
            var frame = new Rectangle { Classes = { "osdframe" }, IsHitTestVisible = false };
            Content = new Grid { Children = { Look, frame } };
        }

        /// The pop-up's own visual (what is measured to place it).
        public Control Look { get; }

        public event Action<PixelPoint>? Dragged;

        public event Action<int, int>? Nudged;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _grab = this.PointToScreen(e.GetPosition(this)) - Position;
            e.Pointer.Capture(this);
            Focus();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (_grab is { } grab) Dragged?.Invoke(this.PointToScreen(e.GetPosition(this)) - grab);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _grab = null;
            e.Pointer.Capture(null);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var by = e.KeyModifiers == KeyModifiers.Shift ? Timing.OsdNudgeLarge : Timing.OsdNudge;
            switch (e.Key)
            {
                case Key.Left: Nudged?.Invoke(-by, 0); break;
                case Key.Right: Nudged?.Invoke(by, 0); break;
                case Key.Up: Nudged?.Invoke(0, -by); break;
                case Key.Down: Nudged?.Invoke(0, by); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
