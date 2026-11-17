using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>A card with a one-line text setting (a name). Calls the apply action when the text changes.</summary>
public sealed partial class TextItem : ObservableObject, IEditorItem
{
    private readonly Action<string> _apply;

    public string Label { get; }

    /// <summary>The grey words shown while the box is empty; null: none.</summary>
    public string? Hint { get; init; }

    [ObservableProperty] private string _text;

    public TextItem(string label, string text, Action<string> apply) => (Label, _text, _apply) = (label, text, apply);

    partial void OnTextChanged(string value) => _apply(value);
}
