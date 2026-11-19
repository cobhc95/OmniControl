namespace OmniControl.App.Features.AppLists.Apps;

/// An app the user added from the Choose-the-action window: its stored id, the name its "Open ..." action shows and its program.
public sealed record UserApp(string Id, string Title, string Path);

/// What the user changed about the "Open ..." app actions: the programs added and the built-in apps hidden from the lists (app-actions.json).
public sealed class AppActionsSettings
{
    public List<UserApp> Added { get; set; } = new();

    /// Ids of the catalogue's apps the user took out of the lists.
    public List<string> Hidden { get; set; } = new();
}
