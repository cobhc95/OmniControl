namespace OmniControl.App.Shared.Ui.Pages;

/// A pick-one setting browsed with Left/Right (or the wheel). A words gallery is the ‹ value › selector of a few named choices. A picture gallery (<see cref="GalleryItem(string, IReadOnlyList{string}, int, Func{Control}, Action{int})"/>:
/// the OSD style, a display's orientation) shows a live preview between arrow buttons, rebuilt whenever the choice changes or
/// <see cref="Refresh"/> is called. A colour gallery (<see cref="GalleryItem(string, IReadOnlyList{string}, int, Func{int, ValueTuple{IBrush, SwatchMark}}, Action{int})"/>: Theme, Accent, Background, the tray icon's, the OSD's and the
/// gadget's colours) is THE swatch button: the chosen dot and its name, opening a pop-down grid of every dot (Pages/GalleryRow).
public sealed partial class GalleryItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<int> _apply;
    private readonly Func<Control>? _preview;
    private readonly Func<int, (IBrush? Fill, SwatchMark Mark)>? _dot;

    public string Label { get; }
    public IReadOnlyList<string> Options { get; }

    /// True: the card takes half the width and may share a row with the next half-width card.
    public bool Half { get; init; }

    /// A fact about the setting the label does not say, shown as the card's tooltip; null: none.
    public string? Tip { get; init; }

    /// Something shown at the card's right end beside the gallery (a <see cref="Features.Hotkeys.SuggestedKeysSwitch"/>), drawn by the data template of its type; null: nothing.
    public object? Extra { get; init; }

    /// The option the pop-down editor edits (the accent gallery's "Custom"): opening the editor picks it first; -1: none.
    public int EditorIndex { get; init; } = -1;

    /// Makes the pop-down editor's content, fresh at each opening; it is given the action that closes the pop-down. Null: no editor.
    public Func<Action, Control>? Editor { get; init; }

    /// The words of the muted row under a colour gallery's dots that opens <see cref="Editor"/> ("Custom colour…").
    public string? EditorText { get; init; }

    /// Raised when the editor should open; the gallery's view opens it under itself.
    public event Action? EditorRequested;

    /// Picks the editor's option (its colour applies at once) and opens the editor.
    public void OpenEditor()
    {
        if (Editor is null) return;
        if (EditorIndex >= 0) Index = EditorIndex;
        EditorRequested?.Invoke();
    }

    /// Greyed out, with a reason, while the choice cannot be made or read (a display Windows would not report on).
    public Availability Availability { get; } = new();

    /// The card's tooltip: why it is unavailable, else <see cref="Tip"/>.
    public string? Hint => Availability.Reason ?? Tip;

    [ObservableProperty] private int _index;

    private bool _loading;

    public string Name => Options[Index];

    /// A fresh visual for the current choice (a picture gallery); null for a colour or words gallery.
    public Control? Preview => _preview?.Invoke();

    /// A picture gallery: its name is the small muted caption under the picture (a words gallery shows it as the value itself).
    public bool HasPreview => _preview is not null;

    /// A colour gallery: drawn as the swatch button (full or half width) instead of the picture between arrows.
    public bool HasDots => _dot is not null;
    public bool FullDots => HasDots && !Half;
    public bool HalfDots => HasDots && Half;

    /// The chosen option's dot (the swatch button shows it); null for a picture gallery.
    public SwatchItem? Current => _dot is null ? null : Dot(Index);

    /// Every option's dot for the pop-down grid, fresh (the colours may follow the theme or the accent); a dot's pick chooses it.
    public IReadOnlyList<SwatchItem> DotItems() => Enumerable.Range(0, Options.Count).Select(Dot).ToList();

    public GalleryItem(string label, IReadOnlyList<string> options, int index, Func<Control> preview, Action<int> apply)
        : this(label, options, index, apply, preview, null)
    {
    }

    /// A words gallery: the choice's name alone between the arrows (the gadget's Position).
    public GalleryItem(string label, IReadOnlyList<string> options, int index, Action<int> apply)
        : this(label, options, index, apply, null, null)
    {
    }

    private GalleryItem(string label, IReadOnlyList<string> options, int index, Action<int> apply, Func<Control>? preview, Func<int, (IBrush? Fill, SwatchMark Mark)>? dot)
    {
        Label = label;
        Options = options;
        _index = Math.Clamp(index, 0, options.Count - 1);
        _preview = preview;
        _dot = dot;
        _apply = apply;
        Availability.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Hint));
    }

    /// A colour gallery: <paramref name="dot"/> gives option i's fill (null = "follow the text colour") and mark.
    public GalleryItem(string label, IReadOnlyList<string> options, int index, Func<int, (IBrush? Fill, SwatchMark Mark)> dot, Action<int> apply)
        : this(label, options, index, apply, null, dot)
    {
    }

    private SwatchItem Dot(int i)
    {
        var (fill, mark) = _dot!(i);
        return new SwatchItem(fill, new RelayCommand(() => Index = i), Options[i], mark) { IsSelected = i == Index };
    }

    /// Shows a choice read from elsewhere without applying it.
    public void Load(int index)
    {
        _loading = true;
        Index = Math.Clamp(index, 0, Options.Count - 1);
        _loading = false;
    }

    /// Moves to the next (+1) or previous (-1) option, wrapping around.
    public void Step(int direction) => Index = Wrap.Index(Index + direction, Options.Count);

    /// Redraws the preview (or the chosen dot) after something else it depends on changed.
    public void Refresh()
    {
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Current));
    }

    partial void OnIndexChanged(int value)
    {
        if (!_loading) _apply(value);
        OnPropertyChanged(nameof(Name));
        Refresh();
    }
}
