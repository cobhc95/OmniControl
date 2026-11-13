using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace OmniControl.App.Pages;

/// <summary>Wiring only: the file or folder picker of a <see cref="FileItem"/>, asking for the kind of file the item names.</summary>
public partial class FileRow : UserControl
{
    public FileRow() => InitializeComponent();

    private async void OnChooseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileItem item || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        if (item.Kind.Folder)
        {
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = item.Kind.PickerTitle, AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } folder) item.Choose(folder);
            return;
        }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = item.Kind.PickerTitle,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(item.Kind.TypeName) { Patterns = item.Kind.Patterns } },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) item.Choose(path);
    }
}
