using OmniControl.App.Features.AppLists;

namespace OmniControl.App.Shared.Ui.Pages;

/// A card holding the shared apps scope chip (<see cref="AppScopeChip"/>) for a setting that is limited to apps: its label on the left, the chip on the right.
public sealed class ScopeItem : IEditorItem, IHalfWidth
{
    public ScopeItem(string label, AppScopeModel scope)
    {
        Label = label;
        Scope = scope;
    }

    public string Label { get; }

    /// The scope the chip shows and edits.
    public AppScopeModel Scope { get; }

    /// A fact the label does not say, shown as the card's tooltip; null: none.
    public string? Tip { get; init; }

    /// A muted line under the label; null: none.
    public string? Note { get; init; }

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public bool Half { get; init; }
}
