using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE panel that places blocks in fixed columns: each block sits in the column it names, in the order
/// the blocks are given, whatever their heights, so nothing moves when content changes or reloads. When the panel is too narrow for the
/// columns (each at least <see cref="ColumnMinWidth"/>) every block goes in one column in the given order. Layout only.
public sealed class FixedColumnsPanel : Panel
{
    public static readonly StyledProperty<double> ColumnMinWidthProperty = AvaloniaProperty.Register<FixedColumnsPanel, double>(nameof(ColumnMinWidth));
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<FixedColumnsPanel, double>(nameof(Gap));

    static FixedColumnsPanel() => AffectsMeasure<FixedColumnsPanel>(ColumnMinWidthProperty, GapProperty);

    /// The least width of a column; a panel too narrow for all of them uses one.
    public double ColumnMinWidth { get => GetValue(ColumnMinWidthProperty); set => SetValue(ColumnMinWidthProperty, value); }

    /// The space between the columns.
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private double _columnWidth;
    private readonly List<(Control Block, int Column, double Top)> _placed = new();

    private static int ColumnOf(Control block) => block.DataContext is IColumnPlaced placed ? Math.Max(0, placed.Column) : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var blocks = Children.Where(c => c.IsVisible).ToList();
        var wanted = blocks.Count == 0 ? 1 : blocks.Max(ColumnOf) + 1;
        var columns = Math.Clamp((int)Math.Floor((width + Gap) / (Math.Max(1, ColumnMinWidth) + Gap)), 1, wanted);
        _columnWidth = (width - Gap * (columns - 1)) / columns;
        var heights = new double[columns];
        _placed.Clear();
        foreach (var block in blocks)
        {
            block.Measure(new Size(_columnWidth, double.PositiveInfinity));
            var column = columns == 1 ? 0 : Math.Min(ColumnOf(block), columns - 1);
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
