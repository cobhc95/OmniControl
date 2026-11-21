using Avalonia.Controls.Primitives;

namespace OmniControl.App.Shared.Ui.Controls;

/// THE right-click (or menu key) menu of a view: the shared menu rows (<see cref="MenuRowList"/>) on a <see cref="ListMenuFlyout"/> at the
/// pointer, made on first use and kept; a pick closes it.
public sealed class PointerMenu
{
    private ListMenuFlyout? _menu;
    private MenuRowList? _rows;

    /// Raised when the menu closed, picked or not.
    public event Action? Closed;

    /// Shows <paramref name="rows"/> (with <paramref name="title"/> above them, when given) at the pointer over <paramref name="at"/>.
    public void Show(Control at, IReadOnlyList<MenuRowItem> rows, string? title = null)
    {
        if (_menu is null)
        {
            _rows = new MenuRowList();
            _rows.Picked += (_, _) => _menu?.Hide();
            _menu = new ListMenuFlyout { Placement = PlacementMode.Pointer, Content = _rows };
            _menu.Closed += (_, _) => Closed?.Invoke();
        }
        _rows!.Title = title;
        _rows.ItemsSource = rows;
        _menu.ShowAt(at, showAtPointer: true);
    }
}
