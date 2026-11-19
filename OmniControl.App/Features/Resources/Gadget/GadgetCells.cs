using Avalonia.Controls.Documents;
using Avalonia.Layout;

namespace OmniControl.App.Features.Resources.Gadget;

/// One cell of the gadget: its label (<see cref="GadgetLabel"/>), value words and colour, the widest words it keeps room for, the room after it and its
/// hover tooltip's lines, asked only while the tooltip shows (null: no tooltip); <see cref="Warned"/> past its warn point (the label's coloured icons then take the warn colour too).
public sealed record GadgetCell(string Label, string Value, Color Colour, string Widest, double GapAfter = 0, Func<IReadOnlyList<GadgetTipLine>>? Tip = null, bool Warned = false);

/// One line of a gadget tooltip: words (muted for a detail), or with <see cref="Graph"/> the small line of recent values; <see cref="StartsPart"/> puts a thin line above it.
public sealed record GadgetTipLine(string Text, bool Muted = false, bool StartsPart = false, IReadOnlyList<double>? Graph = null, double GraphMax = 0);

/// How the cells are laid out: lines, text size in points, the gaps in pixels and where an odd last reading goes.
public sealed record GadgetLayout(int Lines, double TextPoints, double ColumnGap, double LineGap, OddReading Odd)
{
    public static GadgetLayout Of(GadgetSettings look) => new(look.Lines, look.TextSize, look.ColumnGap, look.LineGap, look.Odd);
}

/// THE drawing of the gadget's cells, column by column; an unchanged layout keeps its parts and each value keeps its widest width, so nothing jiggles.
public sealed class GadgetCells : Grid
{
    private const double DipsPerPoint = 96d / 72;

    /// The cells and layout to draw when bound (the settings window's preview and layout cards); the taskbar gadget calls <see cref="Draw"/>.
    public static readonly StyledProperty<IReadOnlyList<GadgetCell>?> CellsProperty = AvaloniaProperty.Register<GadgetCells, IReadOnlyList<GadgetCell>?>(nameof(Cells));
    public static readonly StyledProperty<GadgetLayout?> LayoutProperty = AvaloniaProperty.Register<GadgetCells, GadgetLayout?>(nameof(Layout));

    public IReadOnlyList<GadgetCell>? Cells { get => GetValue(CellsProperty); set => SetValue(CellsProperty, value); }

    public GadgetLayout? Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == CellsProperty || change.Property == LayoutProperty) && Cells is { } cells && Layout is { } layout) Draw(cells, layout);
    }

    private readonly List<(Panel Cell, GadgetLabelView Label, TextBlock Reserve, TextBlock Value)> _cells = new();
    private string _key = string.Empty;
    private readonly Dictionary<Control, Func<IReadOnlyList<GadgetTipLine>>> _tips = new();

    public GadgetCells() => Classes.Add("gadgetcells");

    protected override Type StyleKeyOverride => typeof(Grid); // so Grid.gadgetcells (Styles/Tray.axaml) styles it

    public void Draw(IReadOnlyList<GadgetCell> cells, GadgetLayout layout)
    {
        var key = $"{layout}|{string.Join("|", cells.Select(c => $"{c.Label}\u0001{c.Widest}\u0001{c.GapAfter}"))}";
        if (key != _key) Lay(cells, layout, key);
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

    private void Lay(IReadOnlyList<GadgetCell> cells, GadgetLayout layout, string key)
    {
        _key = key;
        Children.Clear();
        _cells.Clear();
        _tips.Clear();
        var rows = Math.Clamp(layout.Lines, 1, Math.Max(1, cells.Count));
        var columns = Math.Max(1, (cells.Count + rows - 1) / rows);
        var inLast = cells.Count - (columns - 1) * rows;
        var odd = inLast < rows && layout.Odd != OddReading.FillTop;
        (RowSpacing, ColumnSpacing) = (layout.LineGap, layout.ColumnGap);
        RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", rows)));
        ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat("Auto", columns)));
        var size = layout.TextPoints * DipsPerPoint;
        // The odd column is one stack over all lines, centred in the gadget's height; a lone odd reading may be drawn larger.
        var oddStack = odd ? new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = layout.LineGap } : null;
        if (oddStack is not null)
        {
            SetRow(oddStack, 0);
            SetRowSpan(oddStack, rows);
            SetColumn(oddStack, columns - 1);
            Children.Add(oddStack);
        }
        var larger = odd && inLast == 1 && layout.Odd == OddReading.Larger ? GadgetSettings.OddLargerScale : 1;
        for (var i = 0; i < cells.Count; i++)
        {
            var inOdd = oddStack is not null && i / rows == columns - 1;
            var made = Cell(cells[i], inOdd ? size * larger : size);
            _cells.Add(made);
            if (inOdd)
            {
                oddStack!.Children.Add(made.Cell);
                continue;
            }
            SetRow(made.Cell, i % rows);
            SetColumn(made.Cell, i / rows);
            Children.Add(made.Cell);
        }
    }

    /// One cell: the label's words and icons, then the value over its unseen widest copy.
    private (Panel Cell, GadgetLabelView Label, TextBlock Reserve, TextBlock Value) Cell(GadgetCell cell, double size)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = size * GadgetLabelView.PartGap, Margin = new Thickness(0, 0, cell.GapAfter, 0), Background = Brushes.Transparent };
        var label = new GadgetLabelView { Label = cell.Label, TextSize = size };
        var reserve = new TextBlock { Text = cell.Widest, FontSize = size, Classes = { "reserve" } };
        var value = new TextBlock { FontSize = size };
        line.Children.Add(label);
        line.Children.Add(new Panel { Children = { reserve, value }, VerticalAlignment = VerticalAlignment.Center });
        line.AddHandler(ToolTip.ToolTipOpeningEvent, OnTipOpening);
        return (line, label, reserve, value);
    }

    /// Keeps the cell's tooltip lines to fill when it opens; an open tooltip follows each read.
    private void ShowTip(Panel cell, Func<IReadOnlyList<GadgetTipLine>>? tip)
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
