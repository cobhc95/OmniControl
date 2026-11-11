using Avalonia.Controls;
using Avalonia.Input;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public partial class DisplayView : UserControl
{
    public DisplayView()
    {
        InitializeComponent();
    }

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            vm.RunCommand();
            e.Handled = true;
        }
    }
}
