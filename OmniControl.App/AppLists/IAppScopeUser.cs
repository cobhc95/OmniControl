namespace OmniControl.App.AppLists;

/// <summary>
/// Something that stores an apps scope (a group's, a card's): it registers itself with <see cref="AppListStore"/>, which is how the
/// manager window knows what uses a list and can reset the users of a list that is deleted.
/// </summary>
public interface IAppScopeUser
{
    /// <summary>Where it is, as the manager's "Used by" says it ("Mouse › Mouse Buttons").</summary>
    string Where { get; }

    /// <summary>What it stores now: null when it works everywhere (or follows its group).</summary>
    AppScopeSetting? Stored { get; }

    /// <summary>Puts it back to "All apps".</summary>
    void ResetToAll();
}
