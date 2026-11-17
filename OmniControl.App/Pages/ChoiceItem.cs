using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages;

/// <summary>A pick-one setting (theme, input source...). Calls the apply action when changed.</summary>
public sealed partial class ChoiceItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Action<string> _apply;

    public string Label { get; }

    /// <summary>An icon id of the icon library drawn before the label (Other PCs' mirrored cards); null: no icon, the card looks as without.</summary>
    public string? Icon { get; init; }

    /// <summary>True: the card takes half the width and may share a row with the next half-width card.</summary>
    public bool Half { get; init; }

    /// <summary>The entries to choose from; may be replaced while the card is shown (the Mouse and Trackpad page's layer list).</summary>
    [ObservableProperty] private IReadOnlyList<string> _options;

    [ObservableProperty] private string _selected;

    /// <summary>Greyed out, with a reason, while the choice cannot be made (a monitor that does not list its inputs).</summary>
    public Availability Availability { get; } = new();

    /// <summary>For a preset drop-down: what its "Manage presets…" row does (opens the preset manager at the group); null for a plain choice.</summary>
    public Action? Manage { get; init; }

    private bool _loading;

    public ChoiceItem(string label, IReadOnlyList<string> options, string selected, Action<string> apply)
    {
        Label = label;
        Options = options;
        _selected = selected;
        _apply = apply;
    }

    /// <summary>Shows a list and choice read from elsewhere without applying it.</summary>
    public void Load(IReadOnlyList<string> options, string selected)
    {
        _loading = true;
        Options = options;
        Selected = selected;
        _loading = false;
    }

    partial void OnSelectedChanged(string value)
    {
        if (!_loading) _apply(value);
    }
}
