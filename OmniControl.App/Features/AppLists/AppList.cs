namespace OmniControl.App.Features.AppLists;

/// One app of a list: its identity (<see cref="Core.Engine.ProgramIdentity.Normalize"/>: "chrome") and the name shown for it.
public sealed class AppListEntry
{
    public string Program { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// A named list of apps, kept by <see cref="AppListStore"/>. <see cref="Id"/> never changes, so a rename does not break what uses the list.
public sealed class AppList
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<AppListEntry> Apps { get; set; } = new();
}

/// An installed app the manager offers to add: its name and the program file it runs.
public sealed record InstalledApp(string Name, string Program);
