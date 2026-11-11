using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public partial class VolumeView : UserControl
{
    private VolumeViewModel? _vm;

    public VolumeView()
    {
        InitializeComponent();
        var rec = this.FindControl<Border>("Recorder")!;
        rec.KeyDown += (_, e) => { if (_vm?.OnRecorderKey(e.Key, e.KeyModifiers) == true) e.Handled = true; };
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as VolumeViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        };
    }

    private void OnVmChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VolumeViewModel.IsRecording) && _vm!.IsRecording)
            Dispatcher.UIThread.Post(() => this.FindControl<Border>("Recorder")?.Focus(), DispatcherPriority.Background);
    }
}
