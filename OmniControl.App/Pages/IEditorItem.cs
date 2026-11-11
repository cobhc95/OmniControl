namespace OmniControl.App.Pages;

/// <summary>
/// Anything a page shows as an editable card and global search can list (sliders, choices, toggles, galleries, mixer cards, hotkeys).
/// Search only knows this interface, never the concrete card types.
/// </summary>
public interface IEditorItem
{
    /// <summary>The name shown on the card and matched by search.</summary>
    string Label { get; }
}
