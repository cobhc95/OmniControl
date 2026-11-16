using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.About;

/// <summary>Wiring only: Esc and Close shut the window; the list takes the keyboard when it opens.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            CreditList.SelectedIndex = 0;
            (CreditList.ContainerFromIndex(0) as Control ?? CreditList).Focus();
        };
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
