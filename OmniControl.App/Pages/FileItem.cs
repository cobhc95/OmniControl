using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Services;

namespace OmniControl.App.Pages;

/// <summary>
/// What a file card's picker asks for: its title, the name of the file type and the patterns it accepts ("*.cur"); or, with
/// <paramref name="Folder"/>, a folder (the type and patterns are then not used).
/// </summary>
public sealed record FileKind(string PickerTitle, string TypeName, IReadOnlyList<string> Patterns, bool Folder = false)
{
    /// <summary>A card that picks a folder.</summary>
    public static FileKind OfFolder(string pickerTitle) => new(pickerTitle, string.Empty, Array.Empty<string>(), Folder: true);
}

/// <summary>
/// A card for a setting that is a file or a folder (one pointer of the pointer scheme, a plug-in folder to add): the file in use, a
/// "Choose…" button that opens the file or folder picker (in the row's code-behind, the one place that may open a picker) and an
/// optional second button that goes back to a default.
/// </summary>
public sealed partial class FileItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<string> _choose;

    public string Label { get; }

    /// <summary>True: the card takes half the width and may share a row with the next half-width card.</summary>
    public bool Half { get; init; }

    public FileKind Kind { get; }

    /// <summary>The picker the card opens: a folder or a file.</summary>
    public PathKind PathKind => Kind.Folder ? PathKind.Folder : PathKind.File;

    /// <summary>The text of the second button ("Default"), or null for none.</summary>
    public string? ResetText { get; }

    public bool HasReset => ResetText is not null;

    public IRelayCommand ResetCommand { get; }

    public Availability Availability { get; } = new();

    /// <summary>The full path in use, shown as the tooltip; empty for none.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FileName))] private string _path = string.Empty;

    /// <summary>What the card shows: the file's name, or <see cref="EmptyText"/>.</summary>
    public string FileName => Path.Length == 0 ? EmptyText : System.IO.Path.GetFileName(Path);

    /// <summary>Shown when no file is in use.</summary>
    public string EmptyText { get; init; } = "No file chosen";

    public FileItem(string label, FileKind kind, Action<string> choose, string? resetText = null, Action? reset = null)
    {
        Label = label;
        Kind = kind;
        _choose = choose;
        ResetText = resetText;
        ResetCommand = new RelayCommand(() => reset?.Invoke());
    }

    /// <summary>The picker returned a file.</summary>
    public void Choose(string path) => _choose(path);

    /// <summary>Shows a file read from elsewhere (what is in force now).</summary>
    public void Load(string path) => Path = path;
}
