using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Controls;

/// The items panel of <see cref="BrowserTabStrip"/>: tabs in a row at their own width; when they would not fit the strip (the width its list may take,
/// the list's MaxWidth) they shrink together, the widest first, down to <see cref="MinTabWidth"/> each (words trim); only below that the strip scrolls.
/// Layout only.
public sealed class TabFitPanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<TabFitPanel, double>(nameof(Spacing));
    public static readonly StyledProperty<double> MinTabWidthProperty = AvaloniaProperty.Register<TabFitPanel, double>(nameof(MinTabWidth));

    static TabFitPanel() => AffectsMeasure<TabFitPanel>(SpacingProperty, MinTabWidthProperty);

    public double Spacing { get => GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    /// The least width a tab shrinks to.
    public double MinTabWidth { get => GetValue(MinTabWidthProperty); set => SetValue(MinTabWidthProperty, value); }

    private double[] _widths = Array.Empty<double>();

    protected override Size MeasureOverride(Size available)
    {
        var tabs = Children.Where(c => c.IsVisible).ToList();
        var limit = this.FindAncestorOfType<ListBox>()?.MaxWidth ?? double.PositiveInfinity;
        double height = 0;
        var wanted = new double[tabs.Count];
        for (var i = 0; i < tabs.Count; i++)
        {
            tabs[i].Measure(new Size(double.PositiveInfinity, available.Height));
            wanted[i] = tabs[i].DesiredSize.Width;
            height = Math.Max(height, tabs[i].DesiredSize.Height);
        }
        var gaps = Spacing * Math.Max(0, tabs.Count - 1);
        _widths = Fit(wanted, limit - gaps, MinTabWidth);
        for (var i = 0; i < tabs.Count; i++) tabs[i].Measure(new Size(_widths[i], available.Height));
        return new Size(_widths.Sum() + gaps, height);
    }

    /// The widths: as wanted when they fit <paramref name="room"/>, else the widest cut down to a common share, none below <paramref name="min"/>.
    private static double[] Fit(double[] wanted, double room, double min)
    {
        if (wanted.Length == 0 || wanted.Sum() <= room) return wanted;
        double lo = min, hi = wanted.Max();
        for (var step = 0; step < 24; step++)
        {
            var mid = (lo + hi) / 2;
            if (wanted.Sum(w => Math.Min(w, mid)) > room) hi = mid; else lo = mid;
        }
        return wanted.Select(w => Math.Min(w, lo)).ToArray();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tabs = Children.Where(c => c.IsVisible).ToList();
        double x = 0;
        for (var i = 0; i < tabs.Count && i < _widths.Length; i++)
        {
            tabs[i].Arrange(new Rect(x, 0, _widths[i], finalSize.Height));
            x += _widths[i] + Spacing;
        }
        return finalSize;
    }
}
