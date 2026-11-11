using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages.Displays;

public partial class DisplaysPage : UserControl
{
    public DisplaysPage()
    {
        InitializeComponent();
        RowEditing.Attach(DisplayList);
        DisplayList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 || (e.Key == Key.R && e.Source is ListBoxItem))
        {
            (DataContext as DisplaysPageViewModel)?.ReloadCommand.Execute(null);
            e.Handled = true;
        }
    }
}
