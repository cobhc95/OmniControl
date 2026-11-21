namespace OmniControl.App.Shared.Ui.Pages;

/// What the shell knows of a page without making its view-model: the sidebar row, the search's page list, the pickers' sections, the
/// preset manager and the Restore defaults actions read only this, so a page is made the first time it is used (<c>FeaturePage</c>).
/// <param name="Icon">SVG path data (24x24 viewbox) shown next to the title in the sidebar.</param>
/// <param name="NavGroup">The sidebar group the page sits under (one of <see cref="NavGroups"/>); null: no group (a footer page).</param>
/// <param name="Keywords">Extra words the sidebar search matches besides the title.</param>
/// <param name="RestoreKey">The key of the page's "Restore defaults" (<see cref="Restore.IRestorable.Key"/>, also its action's id); null: the page has none.</param>
public sealed record PageInfo(string Title, string Icon, string? NavGroup = null, string Keywords = "", string? RestoreKey = null);
