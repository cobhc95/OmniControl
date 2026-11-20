using Avalonia.Input;

namespace OmniControl.App.Shared.Ui.Controls;

/// The shared behaviour of the colour editor's drawn areas (<see cref="ColorSquare"/>, <see cref="HueBar"/>): mouse first (AGENTS rule 5), a
/// press anywhere sets the value there and a drag follows the pointer; the arrow keys move the handle one step (Shift: ten), so the area still
/// works from the keyboard. Draws the field (by the area), the round handle filled with the colour it points at, and, on keyboard focus, a
/// ring (<see cref="RingBrush"/>, set by the master style in Styles/Menus.axaml). The area's size comes from App.axaml (ColorSquare*, HueBar*).
public abstract class ColorPickArea : Control
{
    /// The focus ring's brush; null = no ring.
    public static readonly StyledProperty<IBrush?> RingBrushProperty = AvaloniaProperty.Register<ColorPickArea, IBrush?>(nameof(RingBrush));

    public IBrush? RingBrush { get => GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }

    private const double HandleRadius = 6, HandleRing = 2, RingThickness = 1.5, RingGap = 2, CornerRadius = 4, ShiftSteps = 10;

    static ColorPickArea() => AffectsRender<ColorPickArea>(RingBrushProperty);

    protected ColorPickArea()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    /// Where the handle is, each from 0 to 1 (left to right, top to bottom).
    protected abstract Point Handle { get; }

    /// The colour under the handle (its fill).
    protected abstract Color HandleColour { get; }

    /// The pointer is at (<paramref name="x"/>, <paramref name="y"/>), each from 0 to 1: set the value there.
    protected abstract void MoveTo(double x, double y);

    /// An arrow key: move by <paramref name="dx"/>, <paramref name="dy"/> steps of the area's own unit; false when the area does not move that way.
    protected abstract bool Nudge(int dx, int dy);

    /// Draws the colour field into <paramref name="field"/> (already clipped to its rounded corners).
    protected abstract void DrawField(DrawingContext context, Rect field);

    /// The field: the bounds less the room the handle and the focus ring need, so neither is clipped at the edges.
    private Rect Field => new Rect(Bounds.Size).Deflate(HandleRadius);

    public override void Render(DrawingContext context)
    {
        var field = Field;
        if (field.Width <= 0 || field.Height <= 0) return;
        using (context.PushClip(new RoundedRect(field, CornerRadius))) DrawField(context, field);
        if (RingBrush is { } ring)
            context.DrawRectangle(null, new Pen(ring, RingThickness), new RoundedRect(field.Inflate(RingGap), CornerRadius + RingGap));

        var at = new Point(field.X + Handle.X * field.Width, field.Y + Handle.Y * field.Height);
        context.DrawEllipse(new SolidColorBrush(HandleColour), new Pen(new SolidColorBrush(Palette.PickerHandleEdge), HandleRing + 1), at, HandleRadius - 1, HandleRadius - 1);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Palette.PickerHandle), HandleRing), at, HandleRadius - 1, HandleRadius - 1);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);
        Focus(NavigationMethod.Pointer);
        Follow(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured == this) Follow(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured == this) e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var steps = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? (int)ShiftSteps : 1;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-steps, 0),
            Key.Right => (steps, 0),
            Key.Up => (0, -steps),
            Key.Down => (0, steps),
            _ => (0, 0),
        };
        if ((dx != 0 || dy != 0) && Nudge(dx, dy)) e.Handled = true;
    }

    private void Follow(Point p)
    {
        var field = Field;
        if (field.Width <= 0 || field.Height <= 0) return;
        MoveTo(Math.Clamp((p.X - field.X) / field.Width, 0, 1), Math.Clamp((p.Y - field.Y) / field.Height, 0, 1));
    }
}
