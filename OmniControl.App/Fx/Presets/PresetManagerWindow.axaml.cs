using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;

namespace OmniControl.App.Fx.Presets;

/// <summary>
/// Wiring only for <see cref="PresetManagerViewModel"/>: the selection, the keys (F2 rename, Delete, Alt+Up / Alt+Down move, Enter show in
/// list, Ctrl+F search, Ctrl+A select all, Esc clears the search then closes), the name box of Save current, the replace question and the
/// import and export file pickers. A dialog over the FX window: mouse first, the buttons and keys also work from the keyboard.
/// </summary>
public partial class PresetManagerWindow : Window
{
    /// <summary>The most an imported preset file may hold (a preset is a few kilobytes).</summary>
    private const int MaxImportBytes = 1024 * 1024;

    private PresetManagerViewModel? _vm;

    public PresetManagerWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKey);
        RowList.SelectionChanged += (_, _) => _vm?.SetSelected(RowList.SelectedItems?.OfType<PresetRowItem>() ?? Enumerable.Empty<PresetRowItem>());
        RowList.DoubleTapped += (_, e) =>
        {
            if (e.Source is TextBlock { DataContext: PresetRowItem { BuiltIn: false } }) _vm?.BeginRenameCommand.Execute(null);
        };
        RowList.AddHandler(KeyDownEvent, OnRenameKey);
        Closed += (_, _) => _vm?.Dispose();
    }

    /// <summary>Shows the manager over <paramref name="owner"/> and waits until it is closed.</summary>
    public static Task ShowAsync(Window owner, PresetManagerViewModel model) =>
        new PresetManagerWindow { DataContext = model }.ShowDialog(owner);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.SelectRequested -= Select;
            _vm.RenameStarted -= FocusRenameBox;
        }
        _vm = DataContext as PresetManagerViewModel;
        if (_vm is null) return;
        _vm.SelectRequested += Select;
        _vm.RenameStarted += FocusRenameBox;
        _vm.Confirm = text => new MessageWindow(new EngineMessage("Manage presets", text, "Replace")).AskAsync(this);
    }

    private void Select(IReadOnlyList<PresetRowItem> rows)
    {
        RowList.SelectedItems?.Clear();
        foreach (var row in rows) RowList.SelectedItems?.Add(row);
        if (rows.Count > 0) RowList.ScrollIntoView(rows[0]);
    }

    private void FocusRenameBox(PresetRowItem row) => Dispatcher.UIThread.Post(() =>
    {
        if (RowList.ContainerFromItem(row) is not Control container) return;
        if (container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is not { } box) return;
        box.Focus();
        box.SelectAll();
    }, DispatcherPriority.Input);

    /// <summary>Enter in a name box applies it, Esc puts the old name back.</summary>
    private async void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Source is not TextBox { DataContext: PresetRowItem { IsEditing: true } row }) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await _vm.CommitRenameAsync(row);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _vm.CancelRename(row);
            RowList.Focus();
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Handled) return;
        var ctrl = e.KeyModifiers == KeyModifiers.Control;
        var alt = e.KeyModifiers == KeyModifiers.Alt;
        var inList = RowList.IsKeyboardFocusWithin && e.Source is not TextBox;
        if (ctrl && e.Key == Key.F) SearchBox.Focus();
        else if (ctrl && e.Key == Key.A && inList) RowList.SelectAll();
        else if (e.Key == Key.Escape && _vm.IsSearching) _vm.Search = string.Empty;
        else if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Enter && _vm.CanShowInList && e.Source is not Button) _vm.ShowInListCommand.Execute(null);
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin) RowList.Focus();
        else if (e.Key == Key.F2 && inList) _vm.BeginRenameCommand.Execute(null);
        else if (e.Key == Key.Delete && inList) _vm.DeleteCommand.Execute(null);
        else if (alt && e.Key == Key.Up && inList) _vm.Move(-1);
        else if (alt && e.Key == Key.Down && inList) _vm.Move(1);
        else return;
        e.Handled = true;
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnSaveCurrentClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is not { } vm) return;
        var name = await new PresetNameWindow(vm.SourceName, vm.NameError, vm.Exists).ShowDialog<string?>(this);
        if (name is not null) await vm.SaveCurrentAsync(name);
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
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
                else vm.Import(file.Name, memory.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                vm.ReportFile($"\"{file.Name}\" could not be read.");
            }
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is not { } vm || vm.ExportOfSelected() is not { } export) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export preset", SuggestedFileName = export.FileName, DefaultExtension = UserPresetStore.FileExtension,
            FileTypeChoices = new[] { PresetFileType }, ShowOverwritePrompt = true,
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(export.Bytes);
            vm.ReportFile("Exported to " + file.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.ReportFile("The preset could not be exported: " + ex.Message);
        }
    }

    private static FilePickerFileType PresetFileType { get; } = new("Preset") { Patterns = new[] { "*." + UserPresetStore.FileExtension } };
}
