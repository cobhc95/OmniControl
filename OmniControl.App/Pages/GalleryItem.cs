using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>
/// A pick-one setting shown as a live preview that is browsed with Left/Right (or the arrow buttons).
/// The preview is rebuilt whenever the choice changes or <see cref="Refresh"/> is called.
/// </summary>
public sealed partial class GalleryItem : ObservableObject, IEditorItem
{
    private readonly Action<int> _apply;
    private readonly Func<Control> _preview;

    public string Label { get; }
    public IReadOnlyList<string> Options { get; }

    [ObservableProperty] private int _index;

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
    }

    /// <summary>Moves to the next (+1) or previous (-1) option, wrapping around.</summary>
    public void Step(int direction) => Index = Services.Wrap.Index(Index + direction, Options.Count);

    /// <summary>Redraws the preview after something else it depends on changed.</summary>
    public void Refresh() => OnPropertyChanged(nameof(Preview));

    partial void OnIndexChanged(int value)
    {
        _apply(value);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Preview));
    }
}
