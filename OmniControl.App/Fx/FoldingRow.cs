using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Fx;

/// <summary>
/// Lays its children out left to right, <see cref="Spacing"/> apart, and leaves out the first one when showing it would leave less
/// than <see cref="Reserve"/> of the width the row is offered (the controls beside the row keep that much). The left-out child keeps
/// its place in the tree but is given no room, and is clipped so nothing of it shows or takes the pointer: the FX window's header
/// keeps only the graph when it is narrow. Layout only; it never moves anything outside the row.
/// </summary>
public sealed class FoldingRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<FoldingRow, double>(nameof(Spacing));
    public static readonly StyledProperty<double> ReserveProperty = AvaloniaProperty.Register<FoldingRow, double>(nameof(Reserve));

    /// <summary>A folded row comes back only with this much room to spare, so a first child whose text changes width a little (a live number) does not flicker in and out.</summary>
    private const double UnfoldMargin = 16;

    private bool _folded;

    static FoldingRow() => AffectsMeasure<FoldingRow>(SpacingProperty, ReserveProperty);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double Reserve
    {
        get => GetValue(ReserveProperty);
        set => SetValue(ReserveProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var shown = Children.Where(c => c.IsVisible).ToList();
        foreach (var child in shown) child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        if (shown.Count == 0) return default;

        var rest = RowWidth(shown.Skip(1));
        var full = shown.Count > 1 ? shown[0].DesiredSize.Width + Spacing + rest : shown[0].DesiredSize.Width;
        var room = availableSize.Width - Reserve - (_folded ? UnfoldMargin : 0);
        _folded = shown.Count > 1 && full > room;
        return new Size(_folded ? rest : full, shown.Max(c => c.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var first = true;
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            if (first && _folded)
            {
                child.ClipToBounds = true;
                child.Arrange(new Rect(0, 0, 0, finalSize.Height));
            }
            else
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width + Spacing;
            }
            first = false;
        }
        return finalSize;
    }

    private double RowWidth(IEnumerable<Control> children)
    {
        var list = children.ToList();
        return list.Count == 0 ? 0 : list.Sum(c => c.DesiredSize.Width) + Spacing * (list.Count - 1);
    }
}
