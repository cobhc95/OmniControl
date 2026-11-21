using System.ComponentModel;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only: one chip and its menu fed by a <see cref="MenuChoiceItem"/> (a choice card's chip, the Resources readings' adapter and sensor
/// chips, the gadget rows' sensor chips). Fills the menu when it opens, follows the item's rows while it is open (live values update the rows
/// in place), closes it after a pick and tells the item when it opens and closes. A view sets it on a chip in markup:
/// <c>pages:MenuChoiceLink.Item="{Binding X}"</c> (and <c>.Title</c> for the menu's title); the link follows the chip in and out of the visual tree.
public sealed class MenuChoiceLink
{
    public static readonly AttachedProperty<MenuChoiceItem?> ItemProperty = AvaloniaProperty.RegisterAttached<ChipMenu, MenuChoiceItem?>("Item", typeof(MenuChoiceLink));
    public static readonly AttachedProperty<string?> TitleProperty = AvaloniaProperty.RegisterAttached<ChipMenu, string?>("Title", typeof(MenuChoiceLink));
    private static readonly AttachedProperty<MenuChoiceLink?> LinkProperty = AvaloniaProperty.RegisterAttached<ChipMenu, MenuChoiceLink?>("Link", typeof(MenuChoiceLink));

    public static MenuChoiceItem? GetItem(ChipMenu chip) => chip.GetValue(ItemProperty);
    public static void SetItem(ChipMenu chip, MenuChoiceItem? item) => chip.SetValue(ItemProperty, item);
    public static string? GetTitle(ChipMenu chip) => chip.GetValue(TitleProperty);
    public static void SetTitle(ChipMenu chip, string? title) => chip.SetValue(TitleProperty, title);

    static MenuChoiceLink()
    {
        ItemProperty.Changed.AddClassHandler<ChipMenu>((chip, e) => LinkOf(chip).Follow(e.NewValue as MenuChoiceItem));
        TitleProperty.Changed.AddClassHandler<ChipMenu>((chip, e) => LinkOf(chip)._list.Title = e.NewValue as string);
    }

    private static MenuChoiceLink LinkOf(ChipMenu chip)
    {
        if (chip.GetValue(LinkProperty) is { } made) return made;
        var link = new MenuChoiceLink(chip, GetTitle(chip));
        chip.SetValue(LinkProperty, link);
        chip.AttachedToVisualTree += (_, _) => link.Attach();
        chip.DetachedFromVisualTree += (_, _) => link.Detach();
        return link;
    }

    private readonly MenuRowList _list;
    private readonly ChipMenu _chip;
    private MenuChoiceItem? _item;
    private Flyout? _flyout;

    private MenuChoiceLink(ChipMenu chip, string? title)
    {
        _chip = chip;
        _list = new MenuRowList { Title = title };
        _list.Picked += (_, _) => _flyout?.Hide();
        chip.Menu = _list;
        chip.MenuOpened += OnOpened;
        chip.MenuClosed += OnClosed;
    }

    private void Follow(MenuChoiceItem? item)
    {
        Detach();
        _item = item;
        if (_chip.IsAttachedToVisualTree()) Attach();
    }

    // Never heard twice (a new item and the attach can both ask), so one Detach always lets the chip go.
    private void Attach()
    {
        if (_item is null) return;
        _item.PropertyChanged -= OnItemChanged;
        _item.PropertyChanged += OnItemChanged;
    }

    private void Detach()
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
