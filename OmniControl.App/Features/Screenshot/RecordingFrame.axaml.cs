using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Features.Screenshot;

/// The recording's frame: a window around the recorded area, as thick as the line on every side, cut down to that line so clicks inside
/// reach the windows underneath, and left out of captures. Layout and wiring only.
public partial class RecordingFrame : Window
{
    private readonly ScreenRect _area;
    private readonly int _edge;

    public RecordingFrame() : this(default, 1) { } // the designer only

    /// <param name="scale">The monitor's scale, so the window has its size before it is first drawn.</param>
    public RecordingFrame(ScreenRect area, double scale)
    {
        InitializeComponent();
        _area = area;
        var thickness = Application.Current?.TryFindResource("RecordFrameThickness", out var t) == true && t is double d ? d : 0;
        _edge = Math.Max(1, (int)Math.Ceiling(thickness * scale)); // the line in physical pixels, at least one
        Position = new PixelPoint(area.X - _edge, area.Y - _edge);
        Width = (area.Width + 2 * _edge) / scale;
        Height = (area.Height + 2 * _edge) / scale;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (TryGetPlatformHandle()?.Handle is not { } handle) return;
        RecordingWindow.ExcludeFromCapture(handle);
        int edge = _edge, outerW = _area.Width + 2 * edge, outerH = _area.Height + 2 * edge;
        RecordingWindow.KeepOnly(handle, new[]
        {
            new ScreenRect(0, 0, outerW, edge), new ScreenRect(0, outerH - edge, outerW, edge),
            new ScreenRect(0, edge, edge, _area.Height), new ScreenRect(outerW - edge, edge, edge, _area.Height),
        });
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        Closed -= OnClosed;
    }
}
