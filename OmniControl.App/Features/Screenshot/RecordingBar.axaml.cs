using Avalonia.Input;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// The recording bar's window: sized to the bar, placed under the recorded area (above or inside when there is no room, as the screenshot
/// overlay's action bar), left out of captures, and given the keys so Esc stops. Layout and wiring only.
public partial class RecordingBar : Window
{
    private readonly RecordingViewModel _model;
    private readonly ScreenRect _area, _monitor;

    public RecordingBar() : this(null!, default, default) { } // the designer only

    /// <param name="area">The recorded area and <paramref name="monitor"/> its monitor, in physical pixels.</param>
    public RecordingBar(RecordingViewModel model, ScreenRect area, ScreenRect monitor)
    {
        InitializeComponent();
        _model = model;
        _area = area;
        _monitor = monitor;
        DataContext = model;
        Position = new PixelPoint(area.Right, area.Bottom);
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var scale = RenderScaling;
        var gap = Application.Current?.TryFindResource("CaptureBarGap", out var g) == true && g is double d ? d * scale : 0;
        double width = Bounds.Width * scale, height = Bounds.Height * scale;
        var (x, y) = SelectionMath.PlaceBar(BarSide.Below, _area.X - _monitor.X, _area.Y - _monitor.Y, _area.Width, _area.Height,
            width, height, _monitor.Width, _monitor.Height, gap);
        Position = new PixelPoint(_monitor.X + (int)Math.Round(x), _monitor.Y + (int)Math.Round(y));
        if (TryGetPlatformHandle()?.Handle is { } handle) RecordingWindow.ExcludeFromCapture(handle);
        Activate(); // Esc stops until another window is clicked
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_model.HandleKey(e.Key)) e.Handled = true;
        base.OnKeyDown(e);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        Closed -= OnClosed;
    }
}
