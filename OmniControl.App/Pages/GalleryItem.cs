using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>
/// A pick-one setting shown as a live preview that is browsed with Left/Right (or the arrow buttons).
/// The preview is rebuilt whenever the choice changes or <see cref="Refresh"/> is called.
/// </summary>
public sealed partial class GalleryItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<int> _apply;
    private readonly Func<Control> _preview;

    public string Label { get; }
    public IReadOnlyList<string> Options { get; }

    /// <summary>True: the card takes half the width and may share a row with the next half-width card (the gallery sits at its right end).</summary>
    public bool Half { get; init; }

    /// <summary>A fact about the setting the label does not say, shown as the card's tooltip; null: none.</summary>
    public string? Tip { get; init; }

    /// <summary>Greyed out, with a reason, while the choice cannot be made or read (a display Windows would not report on).</summary>
    public Availability Availability { get; } = new();

    /// <summary>The card's tooltip: why it is unavailable, else <see cref="Tip"/>.</summary>
    public string? Hint => Availability.Reason ?? Tip;

    [ObservableProperty] private int _index;

    private bool _loading;

    public string Name => Options[Index];

    /// <summary>A fresh visual for the current choice.</summary>
    public Control Preview => _preview();

    public GalleryItem(string label, IReadOnlyList<string> options, int index, Func<Control> preview, Action<int> apply)
    {
        Label = label;
        Options = options;
        _index = Math.Clamp(index, 0, options.Count - 1);
        _preview = preview;
        _apply = apply;
        Availability.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Hint));
    }

    /// <summary>Shows a choice read from elsewhere without applying it.</summary>
    public void Load(int index)
    {
        _loading = true;
        Index = Math.Clamp(index, 0, Options.Count - 1);
        _loading = false;
    }

    /// <summary>Moves to the next (+1) or previous (-1) option, wrapping around.</summary>
    public void Step(int direction) => Index = Services.Wrap.Index(Index + direction, Options.Count);

    /// <summary>Redraws the preview after something else it depends on changed.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Preview));

    partial void OnIndexChanged(int value)
    {
        if (!_loading) _apply(value);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Preview));
    }
}
