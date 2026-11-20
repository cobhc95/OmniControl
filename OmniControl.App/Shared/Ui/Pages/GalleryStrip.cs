using Avalonia.Layout;

namespace OmniControl.App.Shared.Ui.Pages;

/// The one row of a picture gallery's pictures (a display's orientations). It never clips a picture: it takes only the width its card gives
/// it and shows as many WHOLE pictures as fit, a run around the chosen one (the arrows and the wheel move the choice, so the run follows);
/// the pictures left out are not drawn and take no clicks. A picture that carries its index as its Tag and the <see cref="Clickable"/>
/// class is picked by a click (see <see cref="GalleryRow"/>).
public sealed class GalleryStrip : Panel
{
    /// The style class of a picture that can be clicked (it grows a little under the pointer, Theme.axaml).
    public const string Clickable = "gallerypick";

    public GalleryStrip() => HorizontalAlignment = HorizontalAlignment.Center;

    public double Spacing { get; init; }

    /// The chosen picture's index (it is always shown); -1: none, the run starts at the first.
    public int Selected { get; init; }

    private (int First, int Last) _shown;

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        _shown = Fit(availableSize.Width);
        return new Size(RunWidth(_shown), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _shown = Fit(finalSize.Width);
        var x = Math.Max(0, (finalSize.Width - RunWidth(_shown)) / 2);
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var shown = i >= _shown.First && i <= _shown.Last;
            child.Opacity = shown ? 1 : 0;
            child.IsHitTestVisible = shown;
            var size = child.DesiredSize;
            child.Arrange(new Rect(shown ? x : 0, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
            if (shown) x += size.Width + Spacing;
        }
        return finalSize;
    }

    /// The widest run of whole pictures around the chosen one that fits <paramref name="width"/>, growing one to the right, then one to the left.
    private (int First, int Last) Fit(double width)
    {
        if (Children.Count == 0) return (0, -1);
        var start = Math.Clamp(Selected, 0, Children.Count - 1);
        int first = start, last = start;
        var used = Children[start].DesiredSize.Width;
        for (var grew = true; grew;)
        {
            grew = false;
            if (last + 1 < Children.Count && used + Spacing + Children[last + 1].DesiredSize.Width <= width + Fudge) { used += Spacing + Children[++last].DesiredSize.Width; grew = true; }
            if (first > 0 && used + Spacing + Children[first - 1].DesiredSize.Width <= width + Fudge) { used += Spacing + Children[--first].DesiredSize.Width; grew = true; }
        }
        return (first, last);
    }

    private double RunWidth((int First, int Last) run)
    {
        double width = 0;
        for (var i = run.First; i <= run.Last; i++) width += Children[i].DesiredSize.Width + (i > run.First ? Spacing : 0);
        return width;
    }

    private const double Fudge = 0.5; // layout rounding: a run that fits to half a pixel fits
}
