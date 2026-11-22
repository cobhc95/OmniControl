namespace OmniControl.App.Shared.Ui.SettingsImport;

/// Wiring only: shows a <see cref="SettingsWindowViewModel"/>; Esc (the dialog style), Cancel and Close shut it; "Copy details" is the shared <see cref="CopyDetails"/>.
public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyClick(object? sender, RoutedEventArgs e) => await CopyDetails.ToClipboardAsync(this, (DataContext as SettingsWindowViewModel)?.Details);
}