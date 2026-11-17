using OmniControl.App.AppLists;

namespace OmniControl.App.Pages;

/// <summary>A card holding the shared apps scope chip (<see cref="AppScopeChip"/>) for a setting that is limited to apps: its label on the left, the chip on the right.</summary>
public sealed class ScopeItem : IEditorItem, IHalfWidth
{
    public ScopeItem(string label, AppScopeModel scope)
    {
        Label = label;
        Scope = scope;
    }

    public string Label { get; }

    /// <summary>The scope the chip shows and edits.</summary>
    public AppScopeModel Scope { get; }

    /// <summary>A fact the label does not say, shown as the card's tooltip; null: none.</summary>
    public string? Tip { get; init; }

    public bool Half { get; init; }
}
