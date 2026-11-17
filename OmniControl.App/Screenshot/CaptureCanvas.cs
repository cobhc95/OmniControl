using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OmniControl.App.Screenshot;

/// <summary>
/// Draws one monitor's overlay: the frozen picture dimmed, the selection bright with its frame, eight handles and the drawings (clipped to
/// the selection), and turns the pointer into picture pixels for the view-model. Its look (dim, frame, handle size) comes from the master
/// style <c>shot|CaptureCanvas</c> in Styles/Bars.axaml.
/// <para>
/// Built so a 4K or 5K monitor stays light: the picture and the dim over the whole monitor are recorded once and never again; the bright
/// selection is a second draw of the same picture in a layer clipped to the selection, which also holds the finished drawings and the
/// drawing in progress. Moving or resizing the selection only changes that clip and records the thin frame again, so no picture, dim or
/// drawing is recorded again and the renderer repaints around the selection rather than a full-monitor dim. Finished drawings are recorded
/// again only when one comes or goes, the drawing in progress on each pointer move while drawing.
/// </para>
/// </summary>
public sealed class CaptureCanvas : Panel
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

    /// <summary>Where the eight handles sit, as fractions of the selection's width and height.</summary>
    private static readonly (double X, double Y)[] HandleSpots =
        { (0, 0), (0.5, 0), (1, 0), (1, 0.5), (1, 1), (0.5, 1), (0, 1), (0, 0.5) };

    private CaptureOverlayViewModel? _model;

    public CaptureOverlayViewModel? Model
    {
        get => _model;
        set
        {
            if (_model is not null)
            {
                _model.Redraw -= SelectionMoved;
                _model.MarksChanged -= _marks.InvalidateVisual;
                _model.PreviewChanged -= _preview.InvalidateVisual;
            }
            _model = value;
            if (_model is not null)
            {
                _model.Redraw += SelectionMoved;
                _model.MarksChanged += _marks.InvalidateVisual;
                _model.PreviewChanged += _preview.InvalidateVisual;
            }
            _picture.InvalidateVisual();
            _bright.InvalidateVisual();
            _marks.InvalidateVisual();
            SelectionMoved();
        }
    }

    private readonly Control _picture, _dim, _bright, _marks, _preview, _frame;
    private readonly Panel _inside;
    private IPen? _edgePen;

    static CaptureCanvas()
    {
        DimProperty.Changed.AddClassHandler<CaptureCanvas>((canvas, _) => canvas._dim.InvalidateVisual());
        EdgeProperty.Changed.AddClassHandler<CaptureCanvas>((canvas, _) =>
        {
            canvas._edgePen = null;
            canvas._frame.InvalidateVisual();
        });
        HandleSizeProperty.Changed.AddClassHandler<CaptureCanvas>((canvas, _) => canvas._frame.InvalidateVisual());
    }

    public CaptureCanvas()
    {
        Focusable = false;
        Background = Brushes.Transparent; // the panel itself takes the pointer; its layers do not
        _picture = new PictureLayer(this);
        _dim = new DimLayer(this);
        _bright = new PictureLayer(this);
        _marks = new MarksLayer(this);
        _preview = new PreviewLayer(this);
        _inside = new Panel { IsHitTestVisible = false, IsVisible = false };
        _inside.Children.AddRange(new[] { _bright, _marks, _preview });
        _frame = new FrameLayer(this);
        Children.AddRange(new Control[] { _picture, _dim, _inside, _frame });
    }

    /// <summary>Screen units per picture pixel at a width (the window fills the monitor, so this is 1 / the monitor's scale).</summary>
    private double RatioAt(double width) => _model is null || width <= 0 ? 1 : width / _model.PictureWidth;

    private double Ratio => RatioAt(Bounds.Width);

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

    /// <summary>The selection changed, or the drawing in progress began or ended: a new clip, the frame again, the drawing in progress again.</summary>
    private void SelectionMoved()
    {
        ClipToSelection(Bounds.Width);
        _frame.InvalidateVisual();
        _preview.InvalidateVisual();
    }

    /// <summary>The window took its size (it goes full screen after opening): the clip follows the new scale.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        ClipToSelection(finalSize.Width);
        return size;
    }

    /// <summary>Shows the bright layer only inside the selection; hidden while there is none.</summary>
    private void ClipToSelection(double width)
    {
        var area = SelectionArea(RatioAt(width));
        if (area is { } clip) _inside.Clip = new RectangleGeometry(clip);
        if (_inside.IsVisible != area.HasValue) _inside.IsVisible = area.HasValue;
    }

    private Rect? SelectionArea(double k)
    {
        if (_model?.Selection is not { IsEmpty: false } sel) return null;
        return new Rect(sel.X * k, sel.Y * k, sel.Width * k, sel.Height * k);
    }

    /// <summary>Draws marks in picture pixels; the layer they are in is already clipped to the selection.</summary>
    private void DrawMarks(DrawingContext context, IReadOnlyList<Mark> marks)
    {
        using (context.PushTransform(Matrix.CreateScale(Ratio, Ratio)))
            foreach (var mark in marks) mark.Draw(context);
    }

    /// <summary>The same for the one drawing in progress (no list made on every pointer move).</summary>
    private void DrawMark(DrawingContext context, Mark mark)
    {
        using (context.PushTransform(Matrix.CreateScale(Ratio, Ratio)))
            mark.Draw(context);
    }

    /// <summary>The frozen picture at the monitor's size: under the dim, and again as the bright selection inside the clip. Recorded once.</summary>
    private sealed class PictureLayer : Control
    {
        private readonly CaptureCanvas _owner;
        public PictureLayer(CaptureCanvas owner) { _owner = owner; IsHitTestVisible = false; }

        public override void Render(DrawingContext context)
        {
            if (_owner._model is { } model) context.DrawImage(model.Picture, new Rect(Bounds.Size));
        }
    }

    /// <summary>The wash over the whole monitor; the bright selection is drawn over it. Recorded again only when the dim's brush changes.</summary>
    private sealed class DimLayer : Control
    {
        private readonly CaptureCanvas _owner;
        public DimLayer(CaptureCanvas owner) { _owner = owner; IsHitTestVisible = false; }

        public override void Render(DrawingContext context)
        {
            if (_owner.Dim is { } dim) context.FillRectangle(dim, new Rect(Bounds.Size));
        }
    }

    /// <summary>The selection's frame and its eight handles: the only layer recorded again while the selection moves.</summary>
    private sealed class FrameLayer : Control
    {
        private readonly CaptureCanvas _owner;
        public FrameLayer(CaptureCanvas owner) { _owner = owner; IsHitTestVisible = false; }

        public override void Render(DrawingContext context)
        {
            if (_owner.SelectionArea(_owner.Ratio) is not { } area || _owner.Edge is not { } edge) return;
            context.DrawRectangle(null, _owner._edgePen ??= new Pen(edge), area);
            var size = _owner.HandleSize;
            var half = size / 2;
            foreach (var (fx, fy) in HandleSpots)
                context.FillRectangle(edge, new Rect(area.Left + area.Width * fx - half, area.Top + area.Height * fy - half, size, size));
        }
    }

    /// <summary>The finished drawings; recorded again only when one comes or goes (the selection's clip is on the parent layer).</summary>
    private sealed class MarksLayer : Control
    {
        private readonly CaptureCanvas _owner;
        public MarksLayer(CaptureCanvas owner) { _owner = owner; IsHitTestVisible = false; }

        public override void Render(DrawingContext context)
        {
            if (_owner._model is { } model) _owner.DrawMarks(context, model.Marks);
        }
    }

    /// <summary>The one drawing being made: the only thing recorded again on each pointer move while drawing.</summary>
    private sealed class PreviewLayer : Control
    {
        private readonly CaptureCanvas _owner;
        public PreviewLayer(CaptureCanvas owner) { _owner = owner; IsHitTestVisible = false; }

        public override void Render(DrawingContext context)
        {
            if (_owner._model?.Preview is { } preview) _owner.DrawMark(context, preview);
        }
    }
}
