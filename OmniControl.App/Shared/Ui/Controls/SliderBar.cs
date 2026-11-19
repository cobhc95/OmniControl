using Avalonia.Layout;

namespace OmniControl.App.Shared.Ui.Controls;

/// The drawn bar of THE master slider (its template in Styles/Sliders.axaml), either orientation: the rounded track, the accent fill from the
/// minimum (the left end, or the bottom of a vertical slider) to the knob's middle and, for a range that runs through zero (pan, balance, a
/// gain), a centre mark at zero with the fill growing from it to the knob. Drawing only: the slider's own Track places the knob, this follows
/// the same maths (the knob's middle travels from half a knob in to half a knob from the end); every brush and size comes from the master style.
public sealed class SliderBar : Control
{
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(Value));
    public static readonly StyledProperty<Orientation> OrientationProperty = AvaloniaProperty.Register<SliderBar, Orientation>(nameof(Orientation));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<SliderBar, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<SliderBar, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> MarkBrushProperty = AvaloniaProperty.Register<SliderBar, IBrush?>(nameof(MarkBrush));
    public static readonly StyledProperty<double> TrackHeightProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(TrackHeight));
    public static readonly StyledProperty<double> KnobSizeProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(KnobSize));
    public static readonly StyledProperty<double> MarkWidthProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(MarkWidth));
    public static readonly StyledProperty<double> MarkHeightProperty = AvaloniaProperty.Register<SliderBar, double>(nameof(MarkHeight));

    static SliderBar() => AffectsRender<SliderBar>(MinimumProperty, MaximumProperty, ValueProperty, OrientationProperty, TrackBrushProperty, FillProperty,
        MarkBrushProperty, TrackHeightProperty, KnobSizeProperty, MarkWidthProperty, MarkHeightProperty);

    public SliderBar() => IsHitTestVisible = false;

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? MarkBrush { get => GetValue(MarkBrushProperty); set => SetValue(MarkBrushProperty, value); }

    /// The track's thickness (its height when horizontal, its width when vertical).
    public double TrackHeight { get => GetValue(TrackHeightProperty); set => SetValue(TrackHeightProperty, value); }

    /// The knob's size (the slider's Thumb), so the fill ends at the knob's middle.
    public double KnobSize { get => GetValue(KnobSizeProperty); set => SetValue(KnobSizeProperty, value); }

    public double MarkWidth { get => GetValue(MarkWidthProperty); set => SetValue(MarkWidthProperty, value); }
    public double MarkHeight { get => GetValue(MarkHeightProperty); set => SetValue(MarkHeightProperty, value); }

    /// A range through zero is drawn from zero (the centre mark), every other range from its minimum.
    private bool FromCentre => Minimum < 0 && Maximum > 0;

    public override void Render(DrawingContext context)
    {
        var vertical = Orientation == Orientation.Vertical;
        // Drawn along x and across y; a vertical bar swaps them and runs from the bottom up.
        var (length, across) = vertical ? (Bounds.Height, Bounds.Width) : (Bounds.Width, Bounds.Height);
        if (length <= 0 || TrackHeight <= 0) return;
        Rect Place(double from, double size, double top, double thickness) => vertical
            ? new Rect(top, length - from - size, thickness, size)
            : new Rect(from, top, size, thickness);

        var top = (across - TrackHeight) / 2;
        var radius = TrackHeight / 2;
        if (TrackBrush is { } track) context.DrawRectangle(track, null, new RoundedRect(Place(0, length, top, TrackHeight), radius));

        var span = Maximum - Minimum;
        if (span <= 0) return;
        double At(double value) => KnobSize / 2 + (Math.Clamp(value, Minimum, Maximum) - Minimum) / span * Math.Max(0, length - KnobSize);
        var knob = At(Value);

        if (!FromCentre)
        {
            if (Fill is { } fill) context.DrawRectangle(fill, null, new RoundedRect(Place(0, knob, top, TrackHeight), radius));
            return;
        }
        var zero = At(0);
        if (MarkBrush is { } mark) context.DrawRectangle(mark, null, Place(zero - MarkWidth / 2, MarkWidth, (across - MarkHeight) / 2, MarkHeight));
        if (Fill is { } centred) context.DrawRectangle(centred, null, Place(Math.Min(zero, knob), Math.Abs(knob - zero), top, TrackHeight));
    }
}
