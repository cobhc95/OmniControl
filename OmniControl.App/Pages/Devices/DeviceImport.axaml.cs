using Avalonia.Controls;
using Avalonia.Input;

namespace OmniControl.App.Pages.Devices;

/// <summary>Wiring only for the import group's rows: a click on a row, or Space or Enter on the lit one, ticks it.</summary>
public partial class DeviceImport : UserControl
{
    public DeviceImport() => InitializeComponent();

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ImportDeviceItem item) item.Toggle();
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Space or Key.Enter) || (sender as ListBox)?.SelectedItem is not ImportDeviceItem item) return;
        item.Toggle();
        e.Handled = true;
    }
}
