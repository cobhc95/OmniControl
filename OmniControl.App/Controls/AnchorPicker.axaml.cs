using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

namespace OmniControl.App.Controls;

/// <summary>
/// A 3 x 3 grid of squares with one chosen (<see cref="Index"/>, 0 to 8, row by row from the top left). Layout and wiring only: a click
/// picks, <see cref="Walk"/> moves along the grid for the arrow keys (the shared card keys call it too) and <see cref="Step"/> moves
/// through the squares in reading order (the mouse wheel).
/// </summary>
public partial class AnchorPicker : UserControl
{
    private const int Side = 3, Count = Side * Side;

    public static readonly StyledProperty<int> IndexProperty =
        AvaloniaProperty.Register<AnchorPicker, int>(nameof(Index), 0, defaultBindingMode: BindingMode.TwoWay, coerce: (_, value) => Math.Clamp(value, 0, Count - 1));

    private readonly Button[] _cells = new Button[Count];

    public AnchorPicker()
    {
        InitializeComponent();
        for (var i = 0; i < Count; i++)
        {
            var at = i;
            var cell = new Button { Classes = { "anchorcell" }, Focusable = false };
            cell.Click += (_, _) => Index = at;
            _cells[i] = cell;
            Cells.Children.Add(cell);
        }
        ShowChosen();
        GotFocus += (_, _) => Frame.Classes.Set("focused", true);
        LostFocus += (_, _) => Frame.Classes.Set("focused", false);
    }

    /// <summary>The chosen square, row by row from the top left.</summary>
    public int Index { get => GetValue(IndexProperty); set => SetValue(IndexProperty, value); }

    /// <summary>Moves the choice one square along the grid; false at the grid's edge (the key then belongs to whatever is around).</summary>
    public bool Walk(Key key)
    {
        var (column, row) = (Index % Side, Index / Side);
        var (toColumn, toRow) = key switch
        {
            Key.Left => (column - 1, row),
            Key.Right => (column + 1, row),
            Key.Up => (column, row - 1),
            Key.Down => (column, row + 1),
            _ => (column, row),
        };
        if (toColumn is < 0 or >= Side || toRow is < 0 or >= Side || (toColumn, toRow) == (column, row)) return false;
        Index = toRow * Side + toColumn;
        return true;
    }

    /// <summary>Moves the choice through the squares in reading order, stopping at both ends.</summary>
    public void Step(int by) => Index += by;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers == KeyModifiers.None && Walk(e.Key)) e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IndexProperty) ShowChosen();
    }

    private void ShowChosen()
    {
        for (var i = 0; i < Count; i++) _cells[i].Classes.Set("selected", i == Index);
    }
}
