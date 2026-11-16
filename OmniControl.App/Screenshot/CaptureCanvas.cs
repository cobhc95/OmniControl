using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OmniControl.App.Screenshot;

/// <summary>
/// Draws one monitor's overlay: the frozen picture dimmed, the selection bright with its frame, eight handles and the drawings (clipped to
/// the selection), and turns the pointer into picture pixels for the view-model. Its look (dim, frame, handle size) comes from the master
/// style <c>shot|CaptureCanvas</c> in Styles/Bars.axaml.
/// </summary>
public sealed class CaptureCanvas : Control
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

    private CaptureOverlayViewModel? _model;

    public CaptureOverlayViewModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null) _model.Redraw -= InvalidateVisual;
            _model = value;
            if (_model is not null) _model.Redraw += InvalidateVisual;
            InvalidateVisual();
        }
    }

    static CaptureCanvas() => AffectsRender<CaptureCanvas>(DimProperty, EdgeProperty, HandleSizeProperty);

    public CaptureCanvas() => Focusable = false;

    /// <summary>Screen units per picture pixel (the window fills the monitor, so this is 1 / the monitor's scale).</summary>
    private double Ratio => _model is null || Bounds.Width <= 0 ? 1 : Bounds.Width / _model.Image.Width;

    private Point ToPixels(PointerEventArgs e) => e.GetPosition(this) / Ratio;

    private double ReachInPixels => HandleReach / Ratio;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_model is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _model.Press(ToPixels(e), ReachInPixels);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_model is null) return;
        var p = ToPixels(e);
        if (_model.IsDragging) _model.MoveTo(p);
        else ShowCursor(CursorFor(_model.GripAt(p, ReachInPixels)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _model?.Release(ToPixels(e));
    }

    private StandardCursorType? _cursorType;

    /// <summary>Changes the pointer's shape only when it differs, releasing the one it replaces.</summary>
    private void ShowCursor(StandardCursorType type)
    {
        if (_cursorType == type) return;
        _cursorType = type;
        var old = Cursor;
        Cursor = new Cursor(type);
        old?.Dispose();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Model = null;
        Cursor?.Dispose();
        Cursor = null;
        _cursorType = null;
    }

    private StandardCursorType CursorFor(Grip grip) => grip switch
    {
        Grip.Move when _model!.Tool == DrawTool.None => StandardCursorType.SizeAll,
        Grip.Move when _model!.Tool == DrawTool.Text => StandardCursorType.Ibeam,
        Grip.N or Grip.S => StandardCursorType.SizeNorthSouth,
        Grip.E or Grip.W => StandardCursorType.SizeWestEast,
        Grip.NW => StandardCursorType.TopLeftCorner,
        Grip.NE => StandardCursorType.TopRightCorner,
        Grip.SW => StandardCursorType.BottomLeftCorner,
        Grip.SE => StandardCursorType.BottomRightCorner,
        _ => StandardCursorType.Cross,
    };

    public override void Render(DrawingContext context)
    {
        if (_model is null) return;
        var whole = new Rect(Bounds.Size);
        context.DrawImage(_model.Picture, whole);
        if (Dim is { } dim) context.FillRectangle(dim, whole);
        if (_model.Selection is not { IsEmpty: false } sel) return;
        var k = Ratio;
        var area = new Rect(sel.X * k, sel.Y * k, sel.Width * k, sel.Height * k);
        using (context.PushClip(area))
        {
            context.DrawImage(_model.Picture, whole);
            using (context.PushTransform(Matrix.CreateScale(k, k)))
            {
                foreach (var mark in _model.Marks) mark.Draw(context);
                _model.Preview?.Draw(context);
            }
        }
        if (Edge is not { } edge) return;
        context.DrawRectangle(null, new Pen(edge), area);
        var half = HandleSize / 2;
        foreach (var (x, y) in new[]
                 {
                     (area.Left, area.Top), (area.Center.X, area.Top), (area.Right, area.Top), (area.Right, area.Center.Y),
                     (area.Right, area.Bottom), (area.Center.X, area.Bottom), (area.Left, area.Bottom), (area.Left, area.Center.Y),
                 })
            context.FillRectangle(edge, new Rect(x - half, y - half, HandleSize, HandleSize));
    }
}
