using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace OmniControl.App.Fx;

/// <summary>Wiring only: the file picker for the IR Loader's impulse response.</summary>
public partial class IrFileRow : UserControl
{
    public IrFileRow() => InitializeComponent();

    private async void OnChooseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IrFileItem item || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an impulse response",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Impulse responses") { Patterns = new[] { "*.wav", "*.flac", "*.aif", "*.aiff" } } },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) item.Choose(path);
    }
}
