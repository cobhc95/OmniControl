using Avalonia.Controls;
using Avalonia.Input;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public partial class HotkeysView : UserControl
{
    public HotkeysView()
    {
        InitializeComponent();
        var rec = this.FindControl<Border>("Recorder");
        if (rec != null)
            rec.KeyDown += (_, e) =>
            {
                if (DataContext is HotkeysViewModel vm && vm.OnRecorderKey(e.Key, e.KeyModifiers))
                    e.Handled = true;
            };
    }
}
