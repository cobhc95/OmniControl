using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace OmniControl.App.Fx;

/// <summary>Wiring only: the file picker (starting in the folder of the file already chosen) and dropping an impulse response on the card.</summary>
public partial class IrFileRow : UserControl
{
    public IrFileRow()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private async void OnChooseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IrFileItem item || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var folder = item.Path.Length > 0 && System.IO.Path.GetDirectoryName(item.Path) is { Length: > 0 } dir ? await storage.TryGetFolderFromPathAsync(dir) : null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an impulse response",
            AllowMultiple = false,
            SuggestedStartLocation = folder,
            FileTypeFilter = new[] { new FilePickerFileType("Impulse responses") { Patterns = IrFileItem.Extensions.Select(x => "*" + x).ToArray() } },
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) item.Choose(path);
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = DataContext is IrFileItem item && DroppedFile(e, item) is not null ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is IrFileItem item && DroppedFile(e, item) is { } path) item.Choose(path);
    }

    private static string? DroppedFile(DragEventArgs e, IrFileItem item) =>
        e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null && item.Accepts(p));
}
