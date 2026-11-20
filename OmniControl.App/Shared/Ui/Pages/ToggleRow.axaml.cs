namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only: the card's menu link fills its pop-down with the card's rows when it opens and closes it after a pick.
public partial class ToggleRow : UserControl
{
    private Flyout? _open;

    public ToggleRow() => InitializeComponent();

    private void OnMenuOpening(object? sender, EventArgs e)
    {
        if (sender is not ListMenuFlyout { Content: MenuRowList list } flyout || DataContext is not ToggleItem { Menu: { } rows }) return;
        _open = flyout;
        list.ItemsSource = rows();
    }

    private void OnMenuPicked(object? sender, EventArgs e) => _open?.Hide();
}
