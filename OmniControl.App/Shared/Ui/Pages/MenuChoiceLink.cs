using System.ComponentModel;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only: one chip and its menu fed by a <see cref="MenuChoiceItem"/> (a choice card's chip, the Resources readings' adapter and sensor
/// chips). Fills the menu when it opens, follows the item's rows while it is open (live values update the rows in place), closes it after a
/// pick and tells the item when it opens and closes. The owner calls <see cref="Follow"/> on a new data context and <see cref="Attach"/> /
/// <see cref="Detach"/> as it enters and leaves the visual tree.
public sealed class MenuChoiceLink
{
    private readonly MenuRowList _list;
    private readonly Visual _owner;
    private MenuChoiceItem? _item;
    private Flyout? _flyout;

    public MenuChoiceLink(ChipMenu chip, Visual owner, string? title = null)
    {
        _owner = owner;
        _list = new MenuRowList { Title = title };
        _list.Picked += (_, _) => _flyout?.Hide();
        chip.Menu = _list;
        chip.MenuOpened += OnOpened;
        chip.MenuClosed += OnClosed;
    }

    public void Follow(MenuChoiceItem? item)
    {
        Detach();
        _item = item;
        if (_owner.IsAttachedToVisualTree()) Attach();
    }

    public void Attach()
    {
        if (_item is not null) _item.PropertyChanged += OnItemChanged;
    }

    public void Detach()
    {
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MenuChoiceItem.Rows) && _flyout is not null && _item is not null) _list.ItemsSource = _item.Rows;
    }

    private void OnOpened(object? sender, Flyout flyout)
    {
        if (_item is null) return;
        _flyout = flyout;
        _item.OpenChanged?.Invoke(true);
        _list.ItemsSource = _item.Rows;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_flyout is not null) _item?.OpenChanged?.Invoke(false);
        _flyout = null;
    }
}
