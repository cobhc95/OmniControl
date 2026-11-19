using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Resources.Pages;

/// Wiring only: fills the network adapter chip's menu with the card's rows when it opens, redraws it while open when the rows change, and closes it after a pick.
public partial class ReadingRow : UserControl
{
    private readonly MenuRowList _list = new() { Title = NetworkAdapterMenu.Title };
    private MenuChoiceItem? _item;
    private Flyout? _flyout;
    private bool _open;

    public ReadingRow()
    {
        InitializeComponent();
        _list.Picked += (_, _) => _flyout?.Hide();
        AdapterChip.Menu = _list;
        AdapterChip.MenuOpened += OnOpened;
        AdapterChip.MenuClosed += OnClosed;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
        _item = (DataContext as ReadingCard)?.Adapter;
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
        if (e.PropertyName == nameof(MenuChoiceItem.Rows) && _open && _item is not null) _list.ItemsSource = _item.Rows;
    }

    private void OnOpened(object? sender, Flyout flyout)
    {
        if (_item is null) return;
        (_flyout, _open) = (flyout, true);
        _item.OpenChanged?.Invoke(true);
        _list.ItemsSource = _item.Rows;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_open) _item?.OpenChanged?.Invoke(false);
        (_flyout, _open) = (null, false);
    }
}
