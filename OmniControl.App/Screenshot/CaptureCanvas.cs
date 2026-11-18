using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>
/// One monitor's frozen picture in the area overlay: the picture dimmed, the selection bright with its frame and eight handles, and the
/// drawings (shown inside the selection), and the pointer turned into picture pixels for the view-model. Its look (dim, frame, handle
/// size) comes from the master style <c>shot|CaptureCanvas</c> in Styles/Bars.axaml.
/// <para>
/// Hosts Core's <see cref="PictureSurface"/>, a plain Win32 child window painted as Lightshot paints: every change copies only the
/// changed rectangles from a dimmed and a bright copy of the picture made once. Drawn by Avalonia instead, each frame cleared and copied
/// the whole 4K window (about 33 MB) twice and had Windows compose the whole monitor again (see HANDOVER). Because a child window covers
/// whatever Avalonia draws under it, the overlay's bars are pop-ups (<c>Popup.floating</c>). Drawings are painted into the bright copy
/// by <see cref="MarkPainter"/>; the selection, the drawings and the pointer's shape are brought up to date once per frame.
/// </para>
/// </summary>
public sealed class CaptureCanvas : NativeControlHost, IDisposable
{
    public static readonly StyledProperty<IBrush?> DimProperty = AvaloniaProperty.Register<CaptureCanvas, IBrush?>(nameof(Dim));
    public static readonly StyledProperty<IBrush?> EdgeProperty = AvaloniaProperty.Register<CaptureCanvas, IBrush?>(nameof(Edge));
    public static readonly StyledProperty<double> HandleSizeProperty = AvaloniaProperty.Register<CaptureCanvas, double>(nameof(HandleSize), 8);
    public static readonly StyledProperty<double> HandleReachProperty = AvaloniaProperty.Register<CaptureCanvas, double>(nameof(HandleReach), 8);

    /// <summary>The wash over everything outside the selection.</summary>
    public IBrush? Dim { get => GetValue(DimProperty); set => SetValue(DimProperty, value); }

    /// <summary>The selection's frame and handles.</summary>
    public IBrush? Edge { get => GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }

    public double HandleSize { get => GetValue(HandleSizeProperty); set => SetValue(HandleSizeProperty, value); }

    /// <summary>How close to a handle (in screen units) a press grabs it.</summary>
    public double HandleReach { get => GetValue(HandleReachProperty); set => SetValue(HandleReachProperty, value); }

    private readonly CaptureOverlayViewModel _model;
    private readonly PictureSurface _surface;
    private readonly MarkPainter _painter;
    private readonly OncePerFrame _refresh;
    private TopLevel? _window;
    private SurfaceCursor? _cursor;
    private bool _disposed;

    static CaptureCanvas()
    {
        DimProperty.Changed.AddClassHandler<CaptureCanvas>((canvas, _) => canvas.ShowDim());
        EdgeProperty.Changed.AddClassHandler<CaptureCanvas>((canvas, _) => canvas._refresh.Request());
        HandleSizeProperty.Changed.AddClassHandler<CaptureCanvas>((canvas, _) => canvas._refresh.Request());
    }

    public CaptureCanvas(CaptureOverlayViewModel model)
    {
        Focusable = false;
        _model = model;
        _surface = new PictureSurface(model.Picture.PixelSize.Width, model.Picture.PixelSize.Height, Guard.Log);
        _surface.ShowPicture(WritePicture);
        _painter = new MarkPainter(model.Picture, _surface.Paint);
        _refresh = new OncePerFrame(this, "Screenshot overlay: showing the selection and drawings", Refresh);
        _surface.Pressed += OnPressed;
        _surface.Moved += OnMoved;
        _surface.Released += OnReleased;
        _model.Redraw += _refresh.Request;
        _model.MarksChanged += _refresh.Request;
        _model.PreviewChanged += _refresh.Request;
        _refresh.Request();
    }

    /// <summary>The monitor's scale (1 = 96 dpi): the frame and handles are as large in screen units on every monitor.</summary>
    private double Scale => _window?.RenderScaling ?? 1;

    /// <summary>How close to a handle a press grabs it, in picture pixels.</summary>
    private double Reach => HandleReach * Scale;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent) => new PlatformHandle(_surface.Create(parent.Handle), "HWND");

    protected override void DestroyNativeControlCore(IPlatformHandle control) => _surface.Destroy();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this);
        if (_window is not null) _window.ScalingChanged += OnScalingChanged;
        _refresh.Request();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null) _window.ScalingChanged -= OnScalingChanged;
        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScalingChanged(object? sender, EventArgs e) => _refresh.Request();

    /// <summary>Copies the frozen picture into one of the surface's copies.</summary>
    private void WritePicture(IntPtr bits, int stride) =>
        _model.Picture.CopyPixels(new PixelRect(_model.Picture.PixelSize), bits, stride * _model.Picture.PixelSize.Height, stride);

    /// <summary>The dimmed copy again, under the wash the style gives (once when the overlay opens, again only if the theme changes).</summary>
    private void ShowDim()
    {
        if (_disposed || Dim is not ISolidColorBrush dim) return;
        _surface.Dim(WritePicture, Argb(dim));
        _refresh.Request();
    }

    /// <summary>Brings the frame, the selection and the drawings up to date and paints what changed: once per frame.</summary>
    private void Refresh()
    {
        if (_disposed) return;
        if (Edge is ISolidColorBrush edge) _surface.SetLook(Argb(edge), (int)Math.Round(Scale), (int)Math.Round(HandleSize * Scale));
        _surface.SetSelection(_model.Selection);
        _painter.Update(_model.Marks, _model.Preview);
        _surface.PaintNow();
    }

    /// <summary>A brush's colour with its opacity folded into the alpha, as ARGB.</summary>
    private static uint Argb(ISolidColorBrush brush)
    {
        var c = brush.Color;
        var alpha = (uint)Math.Round(c.A * Math.Clamp(brush.Opacity, 0, 1));
        return alpha << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
    }

    private void OnPressed(int x, int y) => _model.Press(new Point(x, y), Reach);

    private void OnMoved(int x, int y)
    {
        var p = new Point(x, y);
        if (_model.IsDragging) _model.MoveTo(p);
        else ShowCursor(CursorFor(_model.GripAt(p, Reach)));
    }

    private void OnReleased(int x, int y) => _model.Release(new Point(x, y));

    /// <summary>Changes the pointer's shape only when it differs.</summary>
    private void ShowCursor(SurfaceCursor cursor)
    {
        if (_cursor == cursor) return;
        _cursor = cursor;
        _surface.ShowCursor(cursor);
    }

    private SurfaceCursor CursorFor(Grip grip) => grip switch
    {
        Grip.Move when _model.Tool == DrawTool.None => SurfaceCursor.Move,
        Grip.Move when _model.Tool == DrawTool.Text => SurfaceCursor.Text,
        Grip.N or Grip.S => SurfaceCursor.NorthSouth,
        Grip.E or Grip.W => SurfaceCursor.WestEast,
        Grip.NW or Grip.SE => SurfaceCursor.NorthWestSouthEast,
        Grip.NE or Grip.SW => SurfaceCursor.NorthEastSouthWest,
        _ => SurfaceCursor.Cross,
    };

    /// <summary>Releases the window and both copies of the picture (the overlay closed). Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _model.Redraw -= _refresh.Request;
        _model.MarksChanged -= _refresh.Request;
        _model.PreviewChanged -= _refresh.Request;
        _surface.Pressed -= OnPressed;
        _surface.Moved -= OnMoved;
        _surface.Released -= OnReleased;
        _surface.Dispose();
    }
}
