namespace OmniControl.App.Shared.Ui.Controls;

/// Lays chips out left to right, wrapping, on at most <see cref="MaxLines"/> lines. The last <see cref="TailCount"/> children are the
/// row's tail (a "+N more" chip, then an add chip) and always show; the first of them shows only while chips are left out. When the
/// chips do not fit, as many as fit with the tail stay, in order, and the rest are left out (given no room and clipped, so nothing of
/// them shows or takes the pointer). <see cref="Folded"/> reports how many were left out, after layout, only when that number changes.
/// Layout only: the row's owner decides what the "+N more" chip says and what it opens.
public sealed class ChipFoldPanel : Panel
{
    public static readonly StyledProperty<int> MaxLinesProperty = AvaloniaProperty.Register<ChipFoldPanel, int>(nameof(MaxLines), 2);
    public static readonly StyledProperty<int> TailCountProperty = AvaloniaProperty.Register<ChipFoldPanel, int>(nameof(TailCount), 2);
    public static readonly StyledProperty<double> ItemSpacingProperty = AvaloniaProperty.Register<ChipFoldPanel, double>(nameof(ItemSpacing));
    public static readonly StyledProperty<double> LineSpacingProperty = AvaloniaProperty.Register<ChipFoldPanel, double>(nameof(LineSpacing));

    private int _shown = int.MaxValue; // chips shown (not counting the tail)
    private bool _foldShown;
    private int _reported;

    static ChipFoldPanel() => AffectsMeasure<ChipFoldPanel>(MaxLinesProperty, TailCountProperty, ItemSpacingProperty, LineSpacingProperty);

    public int MaxLines { get => GetValue(MaxLinesProperty); set => SetValue(MaxLinesProperty, value); }
    public int TailCount { get => GetValue(TailCountProperty); set => SetValue(TailCountProperty, value); }
    public double ItemSpacing { get => GetValue(ItemSpacingProperty); set => SetValue(ItemSpacingProperty, value); }
    public double LineSpacing { get => GetValue(LineSpacingProperty); set => SetValue(LineSpacingProperty, value); }

    /// How many chips the last layout left out.
    public int Hidden => _reported;

    /// Raised on the UI thread, after layout, with the number of chips left out.
    public event Action<int>? Folded;

    private int ChipCount => Math.Max(0, Children.Count - TailCount);

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = availableSize.Width;
        var chips = ChipCount;
        _shown = chips;
        _foldShown = false;
        if (Lines(Placed(chips, fold: false), width).Count > MaxLines)
        {
            _foldShown = true;
            _shown = chips - 1;
            while (_shown > 0 && Lines(Placed(_shown, fold: true), width).Count > MaxLines) _shown--;
        }
        Report(chips - _shown);

        var lines = Lines(Placed(_shown, _foldShown), width);
        var used = lines.Count == 0 ? 0 : lines.Max(l => l.Width);
        var height = lines.Sum(l => l.Height) + Math.Max(0, lines.Count - 1) * LineSpacing;
        return new Size(double.IsInfinity(width) ? used : Math.Min(used, width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var placed = Placed(_shown, _foldShown);
        foreach (var child in Children.Where(c => !placed.Contains(c)))
        {
            child.ClipToBounds = true;
            child.Arrange(new Rect(0, 0, 0, 0));
        }
        var y = 0.0;
        foreach (var line in Lines(placed, finalSize.Width))
        {
            var x = 0.0;
            foreach (var child in line.Children)
            {
                child.ClipToBounds = false;
                var w = Math.Min(child.DesiredSize.Width, finalSize.Width);
                child.Arrange(new Rect(x, y + (line.Height - child.DesiredSize.Height) / 2, w, child.DesiredSize.Height));
                x += w + ItemSpacing;
            }
            y += line.Height + LineSpacing;
        }
        return finalSize;
    }

    /// The children on the row: the first <paramref name="chips"/> chips, the "+N more" chip when <paramref name="fold"/>, and the rest of the tail.
    private List<Control> Placed(int chips, bool fold)
    {
        var placed = Children.Take(chips).ToList();
        var tail = Children.Skip(ChipCount).ToList();
        if (tail.Count > 0 && fold) placed.Add(tail[0]);
        placed.AddRange(tail.Skip(1));
        return placed;
    }

    private sealed record Line(List<Control> Children, double Width, double Height);

    /// The children wrapped into lines no wider than <paramref name="width"/> (a child wider than that gets a line of its own).
    private List<Line> Lines(IEnumerable<Control> children, double width)
    {
        var lines = new List<Line>();
        var current = new List<Control>();
        double x = 0, height = 0;
        foreach (var child in children.Where(c => c.IsVisible && c.DesiredSize.Width > 0))
        {
            var w = child.DesiredSize.Width;
            var needed = current.Count == 0 ? w : x + ItemSpacing + w;
            if (current.Count > 0 && needed > width)
            {
                lines.Add(new Line(current, x, height));
                current = new List<Control>();
                needed = w;
                height = 0;
            }
            current.Add(child);
            x = needed;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        if (current.Count > 0) lines.Add(new Line(current, x, height));
        return lines;
    }

    // Posted, so the owner's change to the "+N more" words (which can change that chip's width) is laid out in a later pass, not inside this one.
    private void Report(int hidden)
    {
        if (hidden == _reported) return;
        _reported = hidden;
        Dispatcher.UIThread.Post(() => Folded?.Invoke(hidden));
    }
}
