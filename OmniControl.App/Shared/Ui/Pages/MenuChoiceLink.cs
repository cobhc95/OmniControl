using System.ComponentModel;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only: one chip and its menu fed by a <see cref="MenuChoiceItem"/> (a choice card's chip, the Resources readings' adapter and sensor
/// chips). Fills the menu when it opens, follows the item's rows while it is open (live values update the rows in place), closes it after a
/// pick and tells the item when it opens and closes. The owner calls <see cref="Follow"/> on a new data context and <see cref="Attach"/> /
/// <see cref="Detach"/> as it enters and leaves the visual tree; a chip inside a card layout (<see cref="CardLayout"/>) is linked by
/// <c>pages:MenuChoiceLink.Item="{Binding}"</c> instead, and is then its own owner.
public sealed class MenuChoiceLink
{
    public static readonly AttachedProperty<MenuChoiceItem?> ItemProperty = AvaloniaProperty.RegisterAttached<MenuChoiceLink, ChipMenu, MenuChoiceItem?>("Item");
    private static readonly AttachedProperty<MenuChoiceLink?> LinkProperty = AvaloniaProperty.RegisterAttached<MenuChoiceLink, ChipMenu, MenuChoiceLink?>("Link");

    static MenuChoiceLink() => ItemProperty.Changed.AddClassHandler<ChipMenu>((chip, e) =>
    {
        if (chip.GetValue(LinkProperty) is not { } link)
        {
            link = new MenuChoiceLink(chip, chip);
            chip.SetValue(LinkProperty, link);
            chip.AttachedToVisualTree += (_, _) => link.Attach();
            chip.DetachedFromVisualTree += (_, _) => link.Detach();
        }
        link.Follow(e.NewValue as MenuChoiceItem);
    });

    public static MenuChoiceItem? GetItem(ChipMenu chip) => chip.GetValue(ItemProperty);
    public static void SetItem(ChipMenu chip, MenuChoiceItem? item) => chip.SetValue(ItemProperty, item);

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

    // Never heard twice (a new data context and the attach can both ask), so one Detach always lets the owner go.
    public void Attach()
    {
        if (_item is null) return;
        _item.PropertyChanged -= OnItemChanged;
        _item.PropertyChanged += OnItemChanged;
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
