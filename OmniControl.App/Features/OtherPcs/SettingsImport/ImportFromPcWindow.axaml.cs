namespace OmniControl.App.Features.OtherPcs.SettingsImport;

/// Wiring only: shows an <see cref="ImportFromPcViewModel"/>; Esc (the dialog style), Cancel and Close shut it; "Copy details" is the shared <see cref="CopyDetails"/>.
public partial class ImportFromPcWindow : Window
{
    public ImportFromPcWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyClick(object? sender, RoutedEventArgs e) => await CopyDetails.ToClipboardAsync(this, (DataContext as ImportFromPcViewModel)?.Details);
}
