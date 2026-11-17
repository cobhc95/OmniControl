using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.VisualTree;
using OmniControl.App.Controls;

namespace OmniControl.App.Pages;

/// <summary>Wiring only: fills the chip's menu with the card's rows when it opens, redraws it while open when the rows change, and closes it after a pick.</summary>
public partial class MenuChoiceRow : UserControl
{
    private readonly Dictionary<ChipMenu, MenuRowList> _lists = new();
    private MenuChoiceItem? _item;
    private MenuRowList? _open;
    private Flyout? _flyout;

    public MenuChoiceRow()
    {
        InitializeComponent();
        foreach (var chip in new[] { FullChip, HalfChip })
        {
            var list = new MenuRowList();
            list.Picked += (_, _) => _flyout?.Hide();
            chip.Menu = _lists[chip] = list;
            chip.MenuOpened += OnOpened;
            chip.MenuClosed += OnClosed;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
        _item = DataContext as MenuChoiceItem;
        if (_item is not null && this.IsAttachedToVisualTree()) _item.PropertyChanged += OnItemChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_item is not null) _item.PropertyChanged += OnItemChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MenuChoiceItem.Rows) && _open is not null && _item is not null) _open.ItemsSource = _item.Rows;
    }

    private void OnOpened(object? sender, Flyout flyout)
    {
        if (sender is not ChipMenu chip || _item is null) return;
        (_flyout, _open) = (flyout, _lists[chip]);
        _open.ItemsSource = _item.Rows;
    }

    private void OnClosed(object? sender, EventArgs e) => (_flyout, _open) = (null, null);
}
