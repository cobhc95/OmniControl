using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Display;

namespace OmniControl.App.Pages.Displays;

/// <summary>One saved picture preset in the presets window: its name and what Apply, Rename and Delete do with it.</summary>
public sealed class DisplayPresetRow
{
    public DisplayPresetRow(string name, Action apply, Action rename, Action delete)
    {
        Name = name;
        ApplyCommand = new RelayCommand(apply);
        RenameCommand = new RelayCommand(rename);
        DeleteCommand = new RelayCommand(delete);
    }

    public string Name { get; }
    public IRelayCommand ApplyCommand { get; }
    public IRelayCommand RenameCommand { get; }
    public IRelayCommand DeleteCommand { get; }
}

/// <summary>
/// What the picture presets window shows: a name box with "Save current" (the current software picture of every display and the
/// desktop's colour effect, kept under that name; a name that exists is replaced), and the saved presets with Apply, Rename and Delete.
/// Rename uses the same box. Names follow <see cref="PresetRules"/>. No windows here: the page gives it how to read and apply the picture.
/// </summary>
public sealed partial class DisplayPresetsModel : ObservableObject, IDisposable
{
    private const string ReplaceNote = "A preset of this name exists: saving replaces it.";

    private readonly DisplaySettingsStore _store;
    private readonly Func<DisplayPreset> _capture;
    private readonly Action<DisplayPreset> _apply;
    private string? _renaming;
    private string _problem = string.Empty;

    [ObservableProperty] private string _name = string.Empty;

    public ObservableCollection<DisplayPresetRow> Rows { get; } = new();

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelRenameCommand { get; }

    /// <param name="capture">Reads the picture the displays show now.</param>
    /// <param name="apply">Shows a saved picture on the displays.</param>
    internal DisplayPresetsModel(DisplaySettingsStore store, Func<DisplayPreset> capture, Action<DisplayPreset> apply)
    {
        (_store, _capture, _apply) = (store, capture, apply);
        SaveCommand = new RelayCommand(Save, () => CanSave);
        CancelRenameCommand = new RelayCommand(EndRename);
        store.PresetsChanged += Fill;
        Fill();
    }

    public bool IsRenaming => _renaming is not null;
    public bool HasNone => Rows.Count == 0;
    public string SaveText => IsRenaming ? "Rename" : "Save current";

    private bool CanSave => Name.Trim().Length > 0 && PresetRules.Error(Name) is null;

    /// <summary>One line under the name box: why the name cannot be used, that saving replaces a preset, or what is being renamed.</summary>
    public string Note
    {
        get
        {
            if (_problem.Length > 0) return _problem;
            var typed = Name.Trim();
            if (typed.Length > 0 && PresetRules.Error(typed) is { } error) return error;
            if (IsRenaming) return $"Type the new name for {_renaming}.";
            return typed.Length > 0 && _store.Find(typed) is not null ? ReplaceNote : string.Empty;
        }
    }

    public bool HasNote => Note.Length > 0;

    partial void OnNameChanged(string value)
    {
        _problem = string.Empty;
        RefreshState();
    }

    private void RefreshState()
    {
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(SaveText));
        OnPropertyChanged(nameof(IsRenaming));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void Save()
    {
        if (_renaming is { } from)
        {
            if (_store.Rename(from, Name.Trim()) is { } problem)
            {
                _problem = problem;
                RefreshState();
                return;
            }
            EndRename();
            return;
        }
        var preset = _capture();
        preset.Name = Name.Trim();
        _store.Save(preset);
        RefreshState(); // the note now says the name exists
    }

    private void BeginRename(string name)
    {
        _renaming = name;
        Name = name;
        RefreshState();
    }

    private void EndRename()
    {
        _renaming = null;
        Name = string.Empty;
        RefreshState();
    }

    private void Fill()
    {
        Rows.Clear();
        foreach (var preset in _store.Presets.ToList())
        {
            var name = preset.Name;
            Rows.Add(new DisplayPresetRow(name, () => _apply(preset), () => BeginRename(name), () => Delete(name)));
        }
        OnPropertyChanged(nameof(HasNone));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(HasNote));
    }

    private void Delete(string name)
    {
        if (_renaming is not null && PresetRules.Same(_renaming, name)) EndRename();
        _store.Delete(name);
    }

    public void Dispose() => _store.PresetsChanged -= Fill;
}
