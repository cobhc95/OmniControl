using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Path = Avalonia.Controls.Shapes.Path;

namespace OmniControl.App.Shared.Ui.Controls;

/// THE drawing of one colour dot (a colour pop-down's dots, the swatch chip's dot, the screenshot overlay's colour button): the fill with a
/// thin edge (so a white or black dot shows on either theme); a null fill is the "follow the text colour" dot (a dashed ring with an A);
/// <see cref="SwatchMark.Windows"/> adds the small Windows mark, <see cref="SwatchMark.Custom"/> draws the fill inside a hue ring;
/// <see cref="IsChosen"/> rings it in the accent (outside its own size, inside the dot's cell padding). Colours are the theme's
/// (BrushHoverEdge, BrushMuted, BrushAccent, BrushHueRing); the size is set by the master styles (SwatchSize, ChipSwatchSize).
public sealed class SwatchDot : Panel
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<SwatchDot, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<SwatchMark> MarkProperty = AvaloniaProperty.Register<SwatchDot, SwatchMark>(nameof(Mark));
    public static readonly StyledProperty<bool> IsChosenProperty = AvaloniaProperty.Register<SwatchDot, bool>(nameof(IsChosen));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<SwatchDot, double>(nameof(Size));

    /// The share of the dot that a custom colour fills inside its hue ring, and that the Windows mark takes.
    private const double CustomShare = 0.5, MarkShare = 0.45;

    public SwatchDot()
    {
        IsHitTestVisible = false; // the button around it takes the click
        Rebuild();
    }

    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public SwatchMark Mark { get => GetValue(MarkProperty); set => SetValue(MarkProperty, value); }
    public bool IsChosen { get => GetValue(IsChosenProperty); set => SetValue(IsChosenProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FillProperty || change.Property == MarkProperty || change.Property == IsChosenProperty || change.Property == SizeProperty) Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        Width = Height = Size;
        if (IsChosen) Children.Add(Ring());
        if (Fill is null)
        {
            Children.Add(Round(Size, null, "BrushMuted", dashed: true));
            var letter = new TextBlock { Text = "A" };
            letter.Classes.Add("swatchletter");
            Children.Add(letter);
            return;
        }
        if (Mark == SwatchMark.Custom)
        {
            Children.Add(Round(Size, null, "BrushHoverEdge", fillKey: "BrushHueRing"));
            Children.Add(Round(Size * CustomShare, Fill, "BrushHoverEdge"));
            return;
        }
        Children.Add(Round(Size, Fill, "BrushHoverEdge"));
        if (Mark == SwatchMark.Windows) Children.Add(WindowsMark());
    }

    private static Ellipse Round(double size, IBrush? fill, string edgeKey, bool dashed = false, string? fillKey = null)
    {
        var round = new Ellipse { Width = size, Height = size, Fill = fill, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        round.Bind(Shape.StrokeProperty, round.GetResourceObservable(edgeKey));
        if (fillKey is not null) round.Bind(Shape.FillProperty, round.GetResourceObservable(fillKey));
        if (dashed) round.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 2, 2 };
        return round;
    }

    // The accent ring of the chosen dot: outside the dot by SwatchRingInset, SwatchRingThickness wide.
    private static Ellipse Ring()
    {
        var ring = new Ellipse();
        ring.Bind(Shape.StrokeProperty, ring.GetResourceObservable("BrushAccent"));
        ring.Bind(Shape.StrokeThicknessProperty, ring.GetResourceObservable("SwatchRingThickness"));
        ring.Bind(MarginProperty, ring.GetResourceObservable("SwatchRingInset"));
        return ring;
    }

    // The Windows mark in the ink that reads on the fill.
    private Path WindowsMark()
    {
        var ink = AccentService.OnAccent(Fill is ISolidColorBrush solid ? solid.Color : Colors.Black);
        var path = new Path
        {
            Width = Size * MarkShare,
            Height = Size * MarkShare,
            Stretch = Stretch.Uniform,
            Fill = new SolidColorBrush(ink),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path.Bind(Path.DataProperty, path.GetResourceObservable("WindowsMarkIcon"));
        return path;
    }
}
