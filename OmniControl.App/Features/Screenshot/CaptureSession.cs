using Avalonia.Media.Imaging;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// One area capture: an overlay window per monitor, each over its own frozen picture. Only one of them holds a selection at a time; closing
/// any closes all and releases every picture and drawing. In the strong "Stay on top" mode a timer puts the overlays back on top and takes
/// the foreground back while they are open (<see cref="OverlayTopmost"/>).
public sealed class CaptureSession
{
    private readonly List<(CaptureOverlay Window, CaptureOverlayViewModel Model)> _overlays = new();
    private readonly StayOnTop _onTop;
    private readonly DispatcherTimer _keepOnTop = new() { Interval = Timing.ScreenshotOnTopRefresh };
    private bool _closing;

    /// Every overlay closed.
    public event Action? Closed;

    /// <param name="recordAtOnce">"Record area": recording starts as soon as a selection is drawn.</param>
    /// <param name="shots">Each monitor and its frozen picture; the session owns the pictures and releases them when it closes.</param>
    /// <param name="layout">The windows and edges behind the overlays (desktop pixels), for snapping and picking a window.</param>
    public CaptureSession(IEnumerable<(ScreenRect Bounds, Bitmap Picture)> shots, DesktopLayout layout, IShotOutput output, StayOnTop onTop, bool recordAtOnce,
        ToolSizes sizes, SnapChoice snap)
    {
        _onTop = onTop;
        foreach (var (bounds, picture) in shots)
        {
            var model = new CaptureOverlayViewModel(picture, bounds, output, Close, recordAtOnce, sizes, new WindowSnap(layout.Within(bounds)), snap);
            model.SelectionStarted += OnSelectionStarted;
            model.HoverStarted += OnHoverStarted;
            _overlays.Add((new CaptureOverlay(model, bounds, onTop == StayOnTop.Strong), model));
        }
        _keepOnTop.Tick += OnKeepOnTop;
    }

    public void Show()
    {
        foreach (var (window, _) in _overlays)
        {
            window.Closed += OnWindowClosed;
            window.Show();
        }
        var underPointer = ScreenCapture.MonitorUnderPointer();
        (_overlays.FirstOrDefault(o => o.Window.Monitor == underPointer).Window ?? _overlays[0].Window).TakeFocus();
        if (_onTop == StayOnTop.Strong) _keepOnTop.Start();
    }

    private void OnSelectionStarted(CaptureOverlayViewModel started)
    {
        foreach (var (_, model) in _overlays)
            if (model != started) model.DropSelection();
    }

    /// The pointer is over another monitor's overlay now: the others stop showing their hovered window.
    private void OnHoverStarted(CaptureOverlayViewModel started)
    {
        foreach (var (_, model) in _overlays)
            if (model != started) model.DropHover();
    }

    private void OnKeepOnTop(object? sender, EventArgs e)
    {
        foreach (var (window, _) in _overlays) window.PutOnTop();
        if (!OverlayTopmost.OwnsForeground()) (_overlays.FirstOrDefault(o => o.Window.IsActive).Window ?? _overlays[0].Window).TakeFocus();
    }

    private void OnWindowClosed(object? sender, EventArgs e) => Close();

    /// Closes every overlay and releases what they hold. Safe to call more than once.
    public void Close()
    {
        if (_closing) return;
        _closing = true;
        _keepOnTop.Stop();
        _keepOnTop.Tick -= OnKeepOnTop;
        foreach (var (window, model) in _overlays)
        {
            window.Closed -= OnWindowClosed;
            window.Close();
            model.SelectionStarted -= OnSelectionStarted;
            model.HoverStarted -= OnHoverStarted;
            model.Dispose();
        }
        _overlays.Clear();
        Closed?.Invoke();
    }
}
