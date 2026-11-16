using Avalonia.Controls;
using OmniControl.App.AppLists;

namespace OmniControl.App.Controls;

/// <summary>Wiring only: the chip opens the "Works in" menu on its model.</summary>
public partial class AppScopeChip : UserControl
{
    public AppScopeChip() => InitializeComponent();

    private void OnOpened(object? sender, Flyout flyout)
    {
        if (DataContext is AppScopeModel model) Menu.Open(flyout, model);
    }

    private void OnClosed(object? sender, EventArgs e) => Menu.Release();
}
