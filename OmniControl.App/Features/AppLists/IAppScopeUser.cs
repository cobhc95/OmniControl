namespace OmniControl.App.Features.AppLists;

/// Something that stores an apps scope (a group's, a card's): it registers itself with <see cref="AppListStore"/>, which is how the
/// manager window knows what uses a list and can reset the users of a list that is deleted.
public interface IAppScopeUser
{
    /// Where it is, as the manager's "Used by" says it ("Mouse › Mouse Buttons").
    string Where { get; }

    /// What it stores now: null when it works everywhere (or follows its group).
    AppScopeSetting? Stored { get; }

    /// Puts it back to "All apps".
    void ResetToAll();
}
