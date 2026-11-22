namespace OmniControl.App.Shared.Ui.Pages;

/// What a file card's picker asks for: its title, the name of the file type and the patterns it accepts ("*.cur"); or, with
/// <paramref name="Folder"/>, a folder (the type and patterns are then not used).
public sealed record FileKind(string PickerTitle, string TypeName, IReadOnlyList<string> Patterns, bool Folder = false)
{
    /// A card that picks a folder.
    public static FileKind OfFolder(string pickerTitle) => new(pickerTitle, string.Empty, Array.Empty<string>(), Folder: true);
}

/// A card for a setting that is a file or a folder (one pointer of the pointer scheme, a plug-in folder to add): the file in use, a
/// "Choose…" button that opens the file or folder picker (in the row's code-behind, the one place that may open a picker) and an
/// optional second button that goes back to a default.
public sealed partial class FileItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<string> _choose;

    public string Label { get; }

    /// True: the card takes half the width and may share a row with the next half-width card.
    public bool Half { get; init; }

    public FileKind Kind { get; }

    /// The picker the card opens: a folder or a file.
    public PathKind PathKind => Kind.Folder ? PathKind.Folder : PathKind.File;

    /// The text of the second button ("Default"), or null for none.
    public string? ResetText { get; }

    public bool HasReset => ResetText is not null;

    public IRelayCommand ResetCommand { get; }

    public Availability Availability { get; } = new();

    /// The full path in use, shown as the tooltip; empty for none.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FileName))] private string _path = string.Empty;

    /// What the card shows: the file's name, or <see cref="EmptyText"/>.
    public string FileName => Path.Length == 0 ? EmptyText : System.IO.Path.GetFileName(Path);

    /// Shown when no file is in use.
    public string EmptyText { get; init; } = "No file chosen";

    public FileItem(string label, FileKind kind, Action<string> choose, string? resetText = null, Action? reset = null)
    {
        Label = label;
        Kind = kind;
        _choose = choose;
        ResetText = resetText;
        ResetCommand = new RelayCommand(() => reset?.Invoke());
    }

    /// A file card made from its search card: the label is the card's, and its muted line is the text shown while no file is chosen.
    public FileItem(SearchCard card, FileKind kind, Action<string> choose, string? resetText = null, Action? reset = null) : this(card.Label, kind, choose, resetText, reset)
    {
        if (card.Note is not null) EmptyText = card.Note;
    }

    public IEnumerable<string> SearchTexts => new[] { EmptyText, ResetText }.Where(t => !string.IsNullOrEmpty(t))!;

    /// The picker returned a file.
    public void Choose(string path) => _choose(path);

    /// Shows a file read from elsewhere (what is in force now).
    public void Load(string path) => Path = path;
}
