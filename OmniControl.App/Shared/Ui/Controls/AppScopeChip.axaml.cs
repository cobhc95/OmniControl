using OmniControl.App.Features.AppLists;

namespace OmniControl.App.Shared.Ui.Controls;

/// Wiring only: the chip opens the "Works in" menu on its model.
public partial class AppScopeChip : UserControl
{
    public AppScopeChip() => InitializeComponent();

    private void OnOpened(object? sender, Flyout flyout)
    {
        if (DataContext is AppScopeModel model) Menu.Open(flyout, model);
    }

    private void OnClosed(object? sender, EventArgs e) => Menu.Release();
}
