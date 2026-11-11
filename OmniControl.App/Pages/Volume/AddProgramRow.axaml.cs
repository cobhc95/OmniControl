using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace OmniControl.App.Pages.Volume;

public partial class AddProgramRow : UserControl
{
    public AddProgramRow() => InitializeComponent();

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddProgramItem item || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a program",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Programs") { Patterns = new[] { "*.exe" } } },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) item.Add(path);
    }
}
