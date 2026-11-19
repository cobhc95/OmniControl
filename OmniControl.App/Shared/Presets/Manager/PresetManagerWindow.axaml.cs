using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Shared.Presets.Manager;

/// Wiring only for <see cref="PresetManagerViewModel"/>: each card's row list gets the shared card keys (<see cref="RowEditing"/>, which
/// also runs the shortcut box) and drag to reorder (<see cref="ListDragReorder"/>); the card it was opened at is scrolled into view; a
/// name box gets the keyboard when renaming starts (Enter or leaving it applies, Esc cancels); the files of export and import are picked
/// here. Keys: Ctrl+F search, F2 rename, Alt+Up / Alt+Down move a row, Esc ends a drag, clears the search, then closes.
public partial class PresetManagerWindow : Window
{
    /// The most a preset file may hold (a preset is a few kilobytes).
    private const int MaxImportBytes = 1024 * 1024;

    private readonly HashSet<ListBox> _wired = new();
    private readonly List<ListDragReorder.Drag> _drags = new();
    private PresetManagerViewModel? _vm;
    private Panel? _menuHolder;

    public PresetManagerWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKey);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.ScrollRequested -= ScrollTo;
            _vm.RenameRequested -= FocusRenameBox;
            _vm.ExportFileRequested -= ExportAsync;
            _vm.ImportFileRequested -= ImportAsync;
        }
        _vm = DataContext as PresetManagerViewModel;
        if (_vm is null) return;
        _vm.ScrollRequested += ScrollTo;
        _vm.RenameRequested += FocusRenameBox;
        _vm.ExportFileRequested += ExportAsync;
        _vm.ImportFileRequested += ImportAsync;
    }

    protected override void OnClosed(EventArgs e)
    {
        DataContext = null; // lets go of the view-model's events
        base.OnClosed(e);
    }

    /// A card's row list appeared: it gets the card keys and drag to reorder, once.
    private void OnCardListLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ListBox list || !_wired.Add(list)) return;
        RowEditing.Attach(list);
        _drags.Add(ListDragReorder.Attach(list, (rows, at) => (list.DataContext as PresetCardViewModel)?.MoveRows(rows, at)));
    }

    private void ScrollTo(PresetCardViewModel card) => Dispatcher.UIThread.Post(() =>
    {
        if (CardList.ContainerFromItem(card) is Control container) container.BringIntoView();
    }, DispatcherPriority.Loaded);

    private void FocusRenameBox(PresetRowViewModel row) => Dispatcher.UIThread.Post(() =>
    {
        if (CardList.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => ReferenceEquals(t.DataContext, row)) is not { } box) return;
        box.BringIntoView();
        box.Focus();
        box.SelectAll();
    }, DispatcherPriority.Loaded);

    private void FocusRow(PresetRowViewModel row) => Dispatcher.UIThread.Post(() =>
    {
        if (CardList.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.DataContext, row)) is { } item) item.Focus();
    }, DispatcherPriority.Loaded);

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Handled) return;
        var focused = FocusManager?.GetFocusedElement();
        var row = (focused as Control)?.FindAncestorOfType<ListBoxItem>(true)?.DataContext as PresetRowViewModel;
        var card = (focused as Control)?.FindAncestorOfType<ListBox>(true)?.DataContext as PresetCardViewModel;
        var inRow = row is not null && focused is not TextBox;
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) SearchBox.Focus();
        else if (e.Key == Key.F2 && inRow) row!.BeginRenameCommand.Execute(null);
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.Alt && inRow && card is not null)
        {
            card.Move(row!, e.Key == Key.Up ? -1 : 1);
            FocusRow(row!);
        }
        else if (e.Key == Key.Escape) { if (!OnEscape(focused)) return; }
        else return;
        e.Handled = true;
    }

    /// Esc: ends a drag, else clears the search, else closes; a part being edited (a shortcut box listening) keeps it. False when it was not used.
    private bool OnEscape(object? focused)
    {
        if (_drags.Any(d => d.Cancel())) return true;
        if (RowEditing.IsEditing(focused)) return false;
        if (_vm is { IsSearching: true } vm) vm.Search = string.Empty;
        else Close();
        return true;
    }

    private void OnNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PresetRowViewModel row }) row.BeginRenameCommand.Execute(null);
    }

    private void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: PresetRowViewModel row }) return;
        if (e.Key == Key.Enter) row.CommitRename();
        else if (e.Key == Key.Escape) row.CancelRename();
        else return;
        e.Handled = true;
        if (!row.IsEditing) FocusRow(row);
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PresetRowViewModel row }) row.CommitRename();
    }

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Parent: Panel holder }) return;
        _menuHolder = holder;
        FlyoutBase.ShowAttachedFlyout(holder);
    }

    private void OnMenuRowClick(object? sender, RoutedEventArgs e)
    {
        if (_menuHolder is { } holder) FlyoutBase.GetAttachedFlyout(holder)?.Hide();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void ExportAsync(PresetCardViewModel card, PresetRowViewModel row)
    {
        if (_vm is not { } vm || card.ExportOf(row) is not { } bytes) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export preset", SuggestedFileName = row.Name, DefaultExtension = UserPresetStore.FileExtension,
            FileTypeChoices = new[] { PresetFileType }, ShowOverwritePrompt = true,
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes);
            vm.ReportFile("Exported to " + file.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.ReportFile("The preset could not be exported: " + ex.Message);
        }
    }

    private async void ImportAsync(PresetCardViewModel card)
    {
        if (_vm is not { } vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import preset", AllowMultiple = true, FileTypeFilter = new[] { PresetFileType },
        });
        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                if (memory.Length > MaxImportBytes) vm.ReportFile($"\"{file.Name}\" is too large to be a preset.");
                else card.ImportFile(file.Name, memory.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                vm.ReportFile($"\"{file.Name}\" could not be read.");
            }
        }
    }

    private static FilePickerFileType PresetFileType { get; } = new("Preset") { Patterns = new[] { "*." + UserPresetStore.FileExtension } };
}
