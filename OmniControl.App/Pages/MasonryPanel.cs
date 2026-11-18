using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Pages;

/// <summary>
/// THE panel that places blocks in up to <see cref="MaxColumns"/> columns that fill independently (docs/mockups/other-pcs-groups-columns.html):
/// each next block goes into the shorter column (the left one on a tie), so reading order is kept as far as it can be and a short block leaves
/// no hole beside a tall one. Columns are never narrower than <see cref="ColumnMinWidth"/>, so a narrow panel is one column. A block is
/// as tall as its own content. Layout only.
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly StyledProperty<int> MaxColumnsProperty = AvaloniaProperty.Register<MasonryPanel, int>(nameof(MaxColumns), 2);
    public static readonly StyledProperty<double> ColumnMinWidthProperty = AvaloniaProperty.Register<MasonryPanel, double>(nameof(ColumnMinWidth));
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<MasonryPanel, double>(nameof(Gap));

    static MasonryPanel() => AffectsMeasure<MasonryPanel>(MaxColumnsProperty, ColumnMinWidthProperty, GapProperty);

    public int MaxColumns { get => GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }

    /// <summary>The least width of a column; a panel too narrow for two of them uses one.</summary>
    public double ColumnMinWidth { get => GetValue(ColumnMinWidthProperty); set => SetValue(ColumnMinWidthProperty, value); }

    /// <summary>The space between the columns.</summary>
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private int _columns = 1;
    private double _columnWidth;
    private readonly List<(Control Block, int Column, double Top)> _placed = new();

    private int ColumnsFor(double width) =>
        Math.Clamp((int)Math.Floor((width + Gap) / (Math.Max(1, ColumnMinWidth) + Gap)), 1, Math.Max(1, MaxColumns));

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        _columns = ColumnsFor(width);
        _columnWidth = (width - Gap * (_columns - 1)) / _columns;
        var heights = new double[_columns];
        _placed.Clear();
        foreach (var block in Children.Where(c => c.IsVisible))
        {
            block.Measure(new Size(_columnWidth, double.PositiveInfinity));
            var column = Array.IndexOf(heights, heights.Min());
            _placed.Add((block, column, heights[column]));
            heights[column] += block.DesiredSize.Height;
        }
        return new Size(width, heights.Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (block, column, top) in _placed)
            block.Arrange(new Rect(column * (_columnWidth + Gap), top, _columnWidth, block.DesiredSize.Height));
        return finalSize;
    }
}
