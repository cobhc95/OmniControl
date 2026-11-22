
namespace OmniControl.App.Features.Traffic.Pages;

/// Wiring only: fills a limit chip's menu with the row's choices when it opens and closes it after a pick; a right-click (or the menu key)
/// opens the row's menu in the shared menu style at the pointer.
public partial class AppNetRow : UserControl
{
    private readonly PointerMenu _menu = new();

    public AppNetRow()
    {
        InitializeComponent();
        UploadMenu.Picked += (_, _) => UploadChip.Flyout?.Hide();
        DownloadMenu.Picked += (_, _) => DownloadChip.Flyout?.Hide();
    }

    private void OnUploadOpened(object? sender, EventArgs e) => UploadMenu.ItemsSource = (DataContext as AppNetItem)?.UploadRows();

    private void OnDownloadOpened(object? sender, EventArgs e) => DownloadMenu.ItemsSource = (DataContext as AppNetItem)?.DownloadRows();

    private void OnRowContext(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not AppNetItem row) return;
        _menu.Show(this, row.MenuRows());
        e.Handled = true;
    }
}
