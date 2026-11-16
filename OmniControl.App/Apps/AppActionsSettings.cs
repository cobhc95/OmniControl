namespace OmniControl.App.Apps;

/// <summary>An app the user added from the Choose-the-action window: its stored id, the name its "Open ..." action shows and its program.</summary>
public sealed record UserApp(string Id, string Title, string Path);

/// <summary>What the user changed about the "Open ..." app actions: the programs added and the built-in apps hidden from the lists (app-actions.json).</summary>
public sealed class AppActionsSettings
{
    public List<UserApp> Added { get; set; } = new();

    /// <summary>Ids of the catalogue's apps the user took out of the lists.</summary>
    public List<string> Hidden { get; set; } = new();
}
