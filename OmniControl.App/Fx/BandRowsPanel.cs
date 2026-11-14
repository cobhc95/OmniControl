using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace OmniControl.App.Fx;

/// <summary>
/// The parametric equaliser's band list as a panel: the rows share the visible height equally but are never lower than
/// <see cref="MinRowHeight"/>; when they do not fit, the surrounding ScrollViewer scrolls. Setting <see cref="SelectedIndex"/> scrolls that row into view.
/// </summary>
public sealed class BandRowsPanel : Panel
{
    /// <summary>The least height of a band row.</summary>
    public const double MinRowHeight = 18;
    private const double Gap = 3;

    public static readonly StyledProperty<double> ViewportHeightProperty = AvaloniaProperty.Register<BandRowsPanel, double>(nameof(ViewportHeight), coerce: (_, v) => double.IsFinite(v) ? Math.Floor(v) : 0);
    public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<BandRowsPanel, int>(nameof(SelectedIndex), -1);

    static BandRowsPanel()
    {
        AffectsMeasure<BandRowsPanel>(ViewportHeightProperty);
        SelectedIndexProperty.Changed.AddClassHandler<BandRowsPanel>((p, _) => p.ScrollToSelected());
    }

    /// <summary>The height the list is shown in (the ScrollViewer's viewport).</summary>
    public double ViewportHeight { get => GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }

    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    private double RowHeight()
    {
        var n = Children.Count;
        if (n == 0) return MinRowHeight;
        // Whole pixels, rounded down: the rows then never add up to a hair more than the viewport, so the ScrollViewer's bar cannot
        // flicker on and off with floating-point noise, and a sub-pixel change of the viewport does not start another layout pass.
        return Math.Max(MinRowHeight, Math.Floor((ViewportHeight - Gap * (n - 1)) / n));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var row = RowHeight();
        foreach (var child in Children) child.Measure(new Size(availableSize.Width, row));
        var n = Children.Count;
        return new Size(0, n == 0 ? 0 : n * row + (n - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var row = RowHeight();
        var y = 0.0;
        foreach (var child in Children)
        {
            child.Arrange(new Rect(0, y, finalSize.Width, row));
            y += row + Gap;
        }
        return finalSize;
    }

    private void ScrollToSelected() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedIndex >= 0 && SelectedIndex < Children.Count) Children[SelectedIndex].BringIntoView();
        }, DispatcherPriority.Loaded);
}
