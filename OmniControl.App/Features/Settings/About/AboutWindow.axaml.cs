using Avalonia.Input;

namespace OmniControl.App.Features.Settings.About;

/// Wiring only: Esc and Close shut the window; the list takes the keyboard when it opens.
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
