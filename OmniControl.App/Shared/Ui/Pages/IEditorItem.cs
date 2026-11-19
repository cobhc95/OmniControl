namespace OmniControl.App.Shared.Ui.Pages;

/// Anything a page shows as an editable card and global search can list (sliders, choices, toggles, galleries, mixer cards, hotkeys).
/// Search only knows this interface, never the concrete card types.
public interface IEditorItem
{
    /// The name shown on the card and matched by search.
    string Label { get; }
}
