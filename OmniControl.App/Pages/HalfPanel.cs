using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Pages;

/// <summary>
/// The items panel of a card list that lets neighbouring <see cref="IHalfWidth"/> cards sit side by side (two per row);
/// every other card takes the full width. Reading order stays the list order (left, then right, then the next row), so keyboard
/// navigation needs no special case. When the list is narrower than <see cref="MinPairWidth"/> every card goes full width.
/// A row is as tall as its tallest card and both cards fill it.
/// </summary>
public sealed class HalfPanel : Panel
{
    private const double Gap = 10, MinPairWidth = 500;

    private static bool IsHalf(Control c) => c.DataContext is IHalfWidth { Half: true };

    /// <summary>Groups the visible cards into rows: one full-width card, one half card, or two half cards.</summary>
    private List<List<Control>> Rows(double width)
    {
        var cards = Children.Where(c => c.IsVisible).ToList();
        var rows = new List<List<Control>>();
        for (var i = 0; i < cards.Count; i++)
        {
            if (width >= MinPairWidth && IsHalf(cards[i]) && i + 1 < cards.Count && IsHalf(cards[i + 1]))
                rows.Add(new List<Control> { cards[i], cards[++i] });
            else rows.Add(new List<Control> { cards[i] });
        }
        return rows;
    }

    private static double CellWidth(List<Control> row, double width) =>
        row.Count == 2 ? (width - Gap) / 2 : width >= MinPairWidth && IsHalf(row[0]) ? (width - Gap) / 2 : width;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double height = 0;
        foreach (var row in Rows(width))
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
        double y = 0;
        foreach (var row in Rows(finalSize.Width))
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
