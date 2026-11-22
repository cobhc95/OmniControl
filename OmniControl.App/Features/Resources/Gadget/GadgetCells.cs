using Avalonia.Controls.Documents;
using Avalonia.Layout;

namespace OmniControl.App.Features.Resources.Gadget;

/// One cell of the gadget: its label (<see cref="GadgetLabel"/>), value words and colour, the widest words it keeps room for, the room after it and its
/// hover tooltip's lines, asked only while the tooltip shows (null: no tooltip); <see cref="Warned"/> past its warn point (the label's coloured icons then take the warn colour too).
public sealed record GadgetCell(string Label, string Value, Color Colour, string Widest, double GapAfter = 0, Func<IReadOnlyList<GadgetTipLine>>? Tip = null, bool Warned = false);

/// One line of a gadget tooltip: words (muted for a detail), or with <see cref="Graph"/> the small line of recent values; <see cref="StartsPart"/> puts a thin line above it.
public sealed record GadgetTipLine(string Text, bool Muted = false, bool StartsPart = false, IReadOnlyList<double>? Graph = null, double GraphMax = 0);

/// How the cells are laid out: lines, text size in points, the gaps in pixels, where an odd last reading goes and the font ("" = the app's own).
public sealed record GadgetLayout(int Lines, double TextPoints, double ColumnGap, double LineGap, OddReading Odd, string Font)
{
    public static GadgetLayout Of(GadgetSettings look) => new(look.Lines, look.TextSize, look.ColumnGap, look.LineGap, look.Odd, look.Font);
}

/// THE drawing of the gadget's cells, column by column; an unchanged layout keeps its parts and each value keeps its widest width, so nothing jiggles.
public sealed class GadgetCells : Grid
{
    private const double DipsPerPoint = 96d / 72;

    /// The cells and layout to draw when bound (the layout tiles and the label editor's line); the taskbar gadget calls <see cref="Draw"/>.
    public static readonly StyledProperty<IReadOnlyList<GadgetCell>?> CellsProperty = AvaloniaProperty.Register<GadgetCells, IReadOnlyList<GadgetCell>?>(nameof(Cells));
    public static readonly StyledProperty<GadgetLayout?> LayoutProperty = AvaloniaProperty.Register<GadgetCells, GadgetLayout?>(nameof(Layout));

    public IReadOnlyList<GadgetCell>? Cells { get => GetValue(CellsProperty); set => SetValue(CellsProperty, value); }

    public GadgetLayout? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }

    /// The room round the outer cells that belongs to their hover zones; the taskbar gadget gives it instead of a margin so the zones leave no gap at its edges.
    public static readonly StyledProperty<Thickness> EdgeProperty = AvaloniaProperty.Register<GadgetCells, Thickness>(nameof(Edge));

    public Thickness Edge { get => GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == CellsProperty || change.Property == LayoutProperty) && Cells is { } cells && Layout is { } layout) Draw(cells, layout);
    }

    private readonly List<(Control Cell, GadgetLabelView Label, TextBlock Reserve, TextBlock Value)> _cells = new();
    private GadgetLayout? _laidLayout;
    private readonly List<(string Label, string Widest, double GapAfter)> _laid = new(); // what the cells were laid out for
    private readonly Dictionary<Control, Func<IReadOnlyList<GadgetTipLine>>> _tips = new();

    public void Draw(IReadOnlyList<GadgetCell> cells, GadgetLayout layout)
    {
        if (!LaidFor(cells, layout)) Lay(cells, layout);
        for (var i = 0; i < cells.Count; i++)
        {
            var (cell, label, reserve, value) = _cells[i];
            label.Warned = cells[i].Warned;
            value.Text = cells[i].Value;
            if (cells[i].Value.Length > reserve.Text!.Length) reserve.Text = cells[i].Value;
            if (TextElement.GetForeground(cell) is not ISolidColorBrush { } brush || brush.Color != cells[i].Colour)
                TextElement.SetForeground(cell, new SolidColorBrush(cells[i].Colour));
            ShowTip(cell, cells[i].Tip);
        }
    }

    // Compared part by part, so a draw with the same layout and cells (every read) builds no text to compare.
    private bool LaidFor(IReadOnlyList<GadgetCell> cells, GadgetLayout layout)
    {
        if (layout != _laidLayout || cells.Count != _laid.Count) return false;
        for (var i = 0; i < cells.Count; i++)
            if (_laid[i] != (cells[i].Label, cells[i].Widest, cells[i].GapAfter)) return false;
        return true;
    }

    /// Lays the cells out as hover zones with no dead space: every gap (between columns, between lines, after a cell) is the padding of the cells beside it, split
    /// in half, and the <see cref="Edge"/> that of the cells at the ends; a cell with nothing below it in its column takes the rest of the column.
    private void Lay(IReadOnlyList<GadgetCell> cells, GadgetLayout layout)
    {
        _laidLayout = layout;
        _laid.Clear();
        _laid.AddRange(cells.Select(c => (c.Label, c.Widest, c.GapAfter)));
        Children.Clear();
        _cells.Clear();
        _tips.Clear();
        var rows = Math.Clamp(layout.Lines, 1, Math.Max(1, cells.Count));
        var columns = Math.Max(1, (cells.Count + rows - 1) / rows);
        var inLast = cells.Count - (columns - 1) * rows;
        var odd = inLast < rows && layout.Odd != OddReading.FillTop;
        if (layout.Font.Length > 0) TextElement.SetFontFamily(this, new FontFamily(layout.Font));
        else ClearValue(TextElement.FontFamilyProperty);
        RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", rows)));
        ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("Auto", columns)));
        var size = layout.TextPoints * DipsPerPoint;
        var (halfColumn, halfLine) = (layout.ColumnGap / 2, layout.LineGap / 2);
        // The odd column is one stack over all lines, centred in the gadget's height (its first and last cell take the room above and below); a lone odd
        // reading may be drawn larger.
        var oddStack = odd ? new Grid { RowDefinitions = new RowDefinitions(inLast == 1 ? "*" : "*" + string.Concat(Enumerable.Repeat(",Auto", inLast - 2)) + ",*") } : null;
        if (oddStack is not null)
        {
            SetRowSpan(oddStack, rows);
            SetColumn(oddStack, columns - 1);
            Children.Add(oddStack);
        }
        var larger = odd && inLast == 1 && layout.Odd == OddReading.Larger ? GadgetSettings.OddLargerScale : 1;
        for (var i = 0; i < cells.Count; i++)
        {
            var (column, row) = (i / rows, i % rows);
            var inOdd = oddStack is not null && column == columns - 1;
            var left = column == 0 ? Edge.Left : halfColumn;
            var right = (column == columns - 1 ? Edge.Right : halfColumn) + cells[i].GapAfter;
            var first = row == 0;
            var last = inOdd ? row == inLast - 1 : row == rows - 1 || i == cells.Count - 1;
            var pad = new Thickness(left, first ? Edge.Top : halfLine, right, last ? Edge.Bottom : halfLine);
            // Placed against the room it takes: the first of an odd stack to the bottom of its zone, the last to the top, one cell in the middle.
            var place = inOdd ? (inLast == 1 ? VerticalAlignment.Center : first ? VerticalAlignment.Bottom : last ? VerticalAlignment.Top : VerticalAlignment.Center) : last && row < rows - 1 ? VerticalAlignment.Top : VerticalAlignment.Stretch;
            var made = Cell(cells[i], inOdd ? size * larger : size, pad, place);
            _cells.Add(made);
            if (inOdd)
            {
                SetRow(made.Cell, row);
                oddStack!.Children.Add(made.Cell);
                continue;
            }
            SetRow(made.Cell, row);
            SetRowSpan(made.Cell, last ? rows - row : 1);
            SetColumn(made.Cell, column);
            Children.Add(made.Cell);
        }
    }

    /// One cell: its hover zone (<paramref name="pad"/> is the gaps it takes), holding the label's words and icons, then the value over its unseen widest copy.
    private (Control Cell, GadgetLabelView Label, TextBlock Reserve, TextBlock Value) Cell(GadgetCell cell, double size, Thickness pad, VerticalAlignment place)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = size * GadgetLabelView.PartGap, VerticalAlignment = place };
        var label = new GadgetLabelView { Label = cell.Label, TextSize = size };
        var reserve = new TextBlock { Text = cell.Widest, FontSize = size, Classes = { "reserve" } };
        var value = new TextBlock { FontSize = size };
        line.Children.Add(label);
        line.Children.Add(new Panel { Children = { reserve, value }, VerticalAlignment = VerticalAlignment.Center });
        var zone = new Border { Background = Brushes.Transparent, Padding = pad, Child = line };
        zone.AddHandler(ToolTip.ToolTipOpeningEvent, OnTipOpening);
        return (zone, label, reserve, value);
    }
    /// Keeps the cell's tooltip lines to fill when it opens; an open tooltip follows each read.
    private void ShowTip(Control cell, Func<IReadOnlyList<GadgetTipLine>>? tip)
    {
        if (tip is null)
        {
            _tips.Remove(cell);
            if (ToolTip.GetTip(cell) is not null) ToolTip.SetTip(cell, null);
            return;
        }
        _tips[cell] = tip;
        if (ToolTip.GetTip(cell) is not StackPanel view) ToolTip.SetTip(cell, view = new StackPanel());
        if (ToolTip.GetIsOpen(cell)) Fill(view, tip());
    }

    private void OnTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is not Control cell || !_tips.TryGetValue(cell, out var tip) || ToolTip.GetTip(cell) is not StackPanel view) return;
        var lines = tip();
        Fill(view, lines);
        e.Cancel = lines.Count == 0; // every detail switched off: nothing to show
    }

    private static void Fill(StackPanel view, IReadOnlyList<GadgetTipLine> lines)
    {
        view.Children.Clear();
        foreach (var line in lines)
        {
            if (line.StartsPart && view.Children.Count > 0) view.Children.Add(new Border { Classes = { "hrule" } });
            if (line.Graph is { } graph)
            {
                view.Children.Add(new Sparkline { Values = graph, Maximum = line.GraphMax });
                continue;
            }
            var text = new TextBlock { Text = line.Text };
            text.Classes.Set("muted", line.Muted);
            view.Children.Add(text);
        }
    }
}
