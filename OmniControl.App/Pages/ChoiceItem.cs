using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>A pick-one setting (theme, input source...). Calls the apply action when changed.</summary>
public sealed partial class ChoiceItem : ObservableObject, IEditorItem
{
    private readonly Action<string> _apply;

    public string Label { get; }
    public IReadOnlyList<string> Options { get; }

    [ObservableProperty] private string _selected;

    public ChoiceItem(string label, IReadOnlyList<string> options, string selected, Action<string> apply)
    {
        Label = label;
        Options = options;
        _selected = selected;
        _apply = apply;
    }

    partial void OnSelectedChanged(string value) => _apply(value);
}
