using System.ComponentModel;

namespace OmniControl.App.Shared.Ui.Controls;

/// Wiring only: shows a <see cref="ChoiceItem"/>'s chosen value on the chip and fills its menu with the options when it opens.
public partial class ChoiceChip : UserControl
{
    private ChoiceItem? _item;
    private Flyout? _menu;

    public ChoiceChip()
    {
        InitializeComponent();
        Rows.Picked += (_, _) => _menu?.Hide();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Follow();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Follow();
    }

    // The item is heard only while the chip is on screen, so a view let go (a released main window) is never held by its model.
    private void Follow()
    {
        var item = VisualRoot is null ? null : DataContext as ChoiceItem;
        if (item == _item) return;
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
        _item = item;
        if (_item is not null) _item.PropertyChanged += OnItemChanged;
        ShowChosen();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChoiceItem.Selected)) ShowChosen();
    }

    private void ShowChosen() => Chip.Text = _item?.Selected;

    private void OnOpened(object? sender, Flyout flyout)
    {
        _menu = flyout;
        if (_item is not { } item) return;
        Rows.Title = item.Label;
        Rows.ItemsSource = item.Options.Select((o, i) => new MenuRowItem(o, string.Empty, o == item.Selected, string.Empty, new RelayCommand(() => item.Selected = o), RuleAbove: item.FirstApart && i == 1)).ToList();
    }

    private void OnClosed(object? sender, EventArgs e) => _menu = null;
}
