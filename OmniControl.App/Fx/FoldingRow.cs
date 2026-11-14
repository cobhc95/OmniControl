using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Fx;

/// <summary>
/// Lays its children out left to right on one line, <see cref="Spacing"/> apart, and never wraps: while the line is wider than the
/// row, the children marked <see cref="FoldLevelProperty"/> 1 are left out, then those marked 2, and so on. A child marked
/// <see cref="ShowsWhenFoldedProperty"/> N shows only while level N is left out (a "…" button that offers what was folded); a child
/// marked <see cref="FillProperty"/> takes the width left over (a gap that pushes the rest to the right, or a text that then trims).
/// A left-out child keeps its place in the tree but is given no room and is clipped, so nothing of it shows or takes the pointer.
/// Layout only; it never moves anything outside the row. Used by the FX window's toolbar and status strip.
/// </summary>
public sealed class FoldingRow : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<FoldingRow, double>(nameof(Spacing));

    /// <summary>0: never folded; 1, 2 …: left out in this order while the row is too wide.</summary>
    public static readonly AttachedProperty<int> FoldLevelProperty = AvaloniaProperty.RegisterAttached<FoldingRow, Control, int>("FoldLevel");

    /// <summary>0: an ordinary child; N: shown only while fold level N is left out.</summary>
    public static readonly AttachedProperty<int> ShowsWhenFoldedProperty = AvaloniaProperty.RegisterAttached<FoldingRow, Control, int>("ShowsWhenFolded");

    /// <summary>The child takes the width the others leave (measured with it, so a text trims).</summary>
    public static readonly AttachedProperty<bool> FillProperty = AvaloniaProperty.RegisterAttached<FoldingRow, Control, bool>("Fill");

    /// <summary>A folded level comes back only with this much room to spare, so a child whose text changes width a little (a live number) does not flicker in and out.</summary>
    private const double UnfoldMargin = 16;

    private int _folded;

    static FoldingRow()
    {
        AffectsMeasure<FoldingRow>(SpacingProperty);
        AffectsParentMeasure<FoldingRow>(FoldLevelProperty, ShowsWhenFoldedProperty, FillProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static int GetFoldLevel(Control c) => c.GetValue(FoldLevelProperty);
    public static void SetFoldLevel(Control c, int value) => c.SetValue(FoldLevelProperty, value);
    public static int GetShowsWhenFolded(Control c) => c.GetValue(ShowsWhenFoldedProperty);
    public static void SetShowsWhenFolded(Control c, int value) => c.SetValue(ShowsWhenFoldedProperty, value);
    public static bool GetFill(Control c) => c.GetValue(FillProperty);
    public static void SetFill(Control c, bool value) => c.SetValue(FillProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        foreach (var child in visible) child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        if (visible.Count == 0) return default;

        var room = availableSize.Width;
        var deepest = visible.Select(GetFoldLevel).DefaultIfEmpty(0).Max();
        var folded = 0;
        while (folded < deepest && WidthAt(visible, folded) > room) folded++;
        // Coming back from a deeper fold needs the margin too, so a level does not flicker at its edge.
        while (folded < _folded && WidthAt(visible, folded) > room - UnfoldMargin) folded++;
        _folded = folded;

        var shown = visible.Where(c => Shows(c, folded)).ToList();
        var fills = shown.Where(GetFill).ToList();
        var fixedWidth = Gaps(shown) + shown.Where(c => !GetFill(c)).Sum(c => c.DesiredSize.Width);
        if (fills.Count > 0 && !double.IsInfinity(room))
        {
            var share = Math.Max(0, room - fixedWidth) / fills.Count;
            foreach (var fill in fills) fill.Measure(new Size(share, availableSize.Height));
        }
        var width = fixedWidth + fills.Sum(c => c.DesiredSize.Width);
        if (fills.Count > 0 && !double.IsInfinity(room)) width = Math.Max(width, room);
        return new Size(width, shown.Max(c => c.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Children.Where(c => c.IsVisible && Shows(c, _folded)).ToList();
        var fills = shown.Where(GetFill).ToList();
        var fixedWidth = Gaps(shown) + shown.Where(c => !GetFill(c)).Sum(c => c.DesiredSize.Width);
        var share = fills.Count == 0 ? 0 : Math.Max(0, finalSize.Width - fixedWidth) / fills.Count;
        var x = 0.0;
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            if (!Shows(child, _folded))
            {
                child.ClipToBounds = true;
                child.Arrange(new Rect(0, 0, 0, finalSize.Height));
                continue;
            }
            child.ClipToBounds = false;
            var w = GetFill(child) ? share : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w + Spacing;
        }
        return finalSize;
    }

    /// <summary>Whether the child is on the line while levels up to <paramref name="folded"/> are left out.</summary>
    private static bool Shows(Control child, int folded)
    {
        var level = GetFoldLevel(child);
        if (level > 0 && level <= folded) return false;
        var when = GetShowsWhenFolded(child);
        return when == 0 || when <= folded;
    }

    /// <summary>The line's width while levels up to <paramref name="folded"/> are left out, every child at its own width (a fill too: a text that would have to trim counts as not fitting).</summary>
    private double WidthAt(IReadOnlyList<Control> visible, int folded)
    {
        var shown = visible.Where(c => Shows(c, folded)).ToList();
        return Gaps(shown) + shown.Sum(c => c.DesiredSize.Width);
    }

    private double Gaps(IReadOnlyCollection<Control> shown) => shown.Count > 1 ? Spacing * (shown.Count - 1) : 0;
}
