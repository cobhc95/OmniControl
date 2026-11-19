namespace OmniControl.App.Shared.Ui.Pages;

/// The items panel of a card list that lets neighbouring <see cref="IHalfWidth"/> cards sit side by side (two per row);
/// every other card takes the full width. Reading order stays the list order (left, then right, then the next row), so keyboard
/// navigation needs no special case. Two half cards pair when the list is at least <see cref="MinPairWidth"/> wide, or, in a narrower
/// list (a column of the Other PCs mirror), when half of it is still at least <see cref="MinHalfWidth"/> (a half card's label wraps to
/// a second line rather than widening it; measuring a card's one-line natural width would never pair them); otherwise each goes full width.
/// A row is as tall as its tallest card and both cards fill it.
public sealed class HalfPanel : Panel
{
    private const double Gap = 10, MinPairWidth = 500, MinHalfWidth = 120;

    /// The rows the last measure made, so arrange places the cards exactly as they were measured.
    private List<List<Control>> _rows = new();

    private static bool IsHalf(Control c) => c.DataContext is IHalfWidth { Half: true };

    /// Groups the visible cards into rows: one full-width card, one half card, or two half cards.
    private List<List<Control>> Rows(double width)
    {
        var cards = Children.Where(c => c.IsVisible).ToList();
        var rows = new List<List<Control>>();
        for (var i = 0; i < cards.Count; i++)
        {
            if (IsHalf(cards[i]) && i + 1 < cards.Count && IsHalf(cards[i + 1]) && Pairs(width))
                rows.Add(new List<Control> { cards[i], cards[++i] });
            else rows.Add(new List<Control> { cards[i] });
        }
        return rows;
    }

    /// True when two half cards share a row: a wide list always; a narrower one while each half is still a usable card width.
    private static bool Pairs(double width) => width >= MinPairWidth || (width - Gap) / 2 >= MinHalfWidth;

    private static double CellWidth(List<Control> row, double width) =>
        row.Count == 2 ? (width - Gap) / 2 : width >= MinPairWidth && IsHalf(row[0]) ? (width - Gap) / 2 : width;

    /// The width the panel was last arranged at. A list inside a scroll viewer is measured with an infinite width, which would
    /// never pair half cards (the Other PCs mirror's mute cards stayed full width); the arranged width is then used and measure re-run.
    private double _arrangedWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? _arrangedWidth : availableSize.Width;
        double height = 0;
        _rows = Rows(width);
        foreach (var row in _rows)
        {
            var cell = CellWidth(row, width);
            var tallest = 0.0;
            foreach (var c in row)
            {
                c.Measure(new Size(cell, double.PositiveInfinity));
                tallest = Math.Max(tallest, c.DesiredSize.Height);
            }
            height += tallest;
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The arranged width is the truth: if the rows it makes differ from the measured ones (measure saw another width), use them now
        // (each card re-measured at its cell) and measure again so the panel's height follows. Measure is asked again only when the
        // arranged width itself changed: a parent that keeps measuring at one width and arranging at another would otherwise loop forever.
        var widthChanged = finalSize.Width != _arrangedWidth;
        _arrangedWidth = finalSize.Width;
        var rows = Rows(finalSize.Width);
        if (rows.Count != _rows.Count)
        {
            foreach (var row in rows)
                foreach (var c in row) c.Measure(new Size(CellWidth(row, finalSize.Width), double.PositiveInfinity));
            _rows = rows;
            if (widthChanged) InvalidateMeasure();
        }
        double y = 0;
        foreach (var row in _rows)
        {
            var cell = CellWidth(row, finalSize.Width);
            var tallest = row.Max(c => c.DesiredSize.Height);
            var x = 0.0;
            foreach (var c in row)
            {
                c.Arrange(new Rect(x, y, cell, tallest));
                x += cell + Gap;
            }
            y += tallest;
        }
        return finalSize;
    }
}
