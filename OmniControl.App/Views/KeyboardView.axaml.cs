using Avalonia.Controls;
using Avalonia.Input;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public partial class KeyboardView : UserControl
{
    public KeyboardView()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (DataContext is KeyboardViewModel vm && vm.IsRecording)
            {
                if (vm.OnKeyPressed(e.Key)) e.Handled = true;
            }
        };
    }
}
