using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace OmniControl.App.Features.Volume.Fx;

/// Wiring only: the file picker (starting in the folder of the file already chosen) and dropping an impulse response on the card.
public partial class IrFileRow : UserControl
{
    public IrFileRow()
    {
        InitializeComponent();
        PathBox.TypeName = "Impulse responses";
        PathBox.Patterns = IrFileItem.Extensions.Select(x => "*" + x).ToArray();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // A typed or browsed path is applied only when the file can be used; otherwise the box goes back to the file in use.
    private void OnChosen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IrFileItem item) return;
        if (File.Exists(PathBox.Text) && item.Accepts(PathBox.Text)) item.Choose(PathBox.Text);
        else PathBox.Text = item.Path;
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
