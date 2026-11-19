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
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
        _item = DataContext as ChoiceItem;
        if (_item is not null) _item.PropertyChanged += OnItemChanged;
        ShowChosen();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_item is not null) _item.PropertyChanged -= OnItemChanged;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_item is not null) { _item.PropertyChanged -= OnItemChanged; _item.PropertyChanged += OnItemChanged; ShowChosen(); }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChoiceItem.Selected)) ShowChosen();
    }

    private void ShowChosen() => (Chip.Text, Chip.ChipToolTip) = (_item?.Selected, _item?.Selected); // the tooltip carries the full name when the words are trimmed

    private void OnOpened(object? sender, Flyout flyout)
    {
        _menu = flyout;
        if (_item is not { } item) return;
        Rows.Title = item.Label;
        Rows.ItemsSource = item.Options.Select((o, i) => new MenuRowItem(o, string.Empty, o == item.Selected, o, new RelayCommand(() => item.Selected = o), RuleAbove: item.FirstApart && i == 1)).ToList();
    }

    private void OnClosed(object? sender, EventArgs e) => _menu = null;
}
