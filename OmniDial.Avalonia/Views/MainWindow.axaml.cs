using Avalonia.Controls;
using Avalonia.Input;
using OmniDial.Avalonia.ViewModels;

namespace OmniDial.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
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
