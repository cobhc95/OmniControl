using Avalonia.Controls;
using Avalonia.Interactivity;
using OmniControl.App.Controls;

namespace OmniControl.App.SettingsImport;

/// <summary>Wiring only: shows an <see cref="ImportFromPcViewModel"/>; Esc (the dialog style), Cancel and Close shut it; "Copy details" is the shared <see cref="CopyDetails"/>.</summary>
public partial class ImportFromPcWindow : Window
{
    public ImportFromPcWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyClick(object? sender, RoutedEventArgs e) => await CopyDetails.ToClipboardAsync(this, (DataContext as ImportFromPcViewModel)?.Details);
}
