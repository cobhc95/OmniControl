using OmniControl.App.Pages;

namespace OmniControl.App.States;

/// <summary>One card of the basic set: its name, its library icon and the page column it always sits in.</summary>
public sealed record CommonGroup(string Name, string Icon, int Column) : IColumnPlaced;

/// <summary>
/// THE list of the cards of the basic set another PC sees (the Other PCs page): each is a name, an icon and a fixed column, and an owner puts its state or
/// action in one by setting <c>Common</c> to the name on its master entry. Everything flagged is shared with every paired PC; nothing else is.
/// The list's order is the reading order of the one-column (narrow) layout and the order inside each column of the wide one
/// (docs/mockups/pc-mirror-fixed-layout.html), so a group never moves when the connection refreshes.
/// </summary>
public static class CommonGroups
{
    public const string Screens = "Screens", Sound = "Sound", SmartDevices = "Smart devices", Power = "Power";

    /// <summary>Left column: what you see and hear. Right column: the PC and the room.</summary>
    private const int Left = 0, Right = 1;

    /// <summary>The card a shared item belongs to: a PC on an older version still flags media items "Now playing", which is now a line inside Sound.</summary>
    public static string? Resolve(string? common) => common == "Now playing" ? Sound : common;

    public static IReadOnlyList<CommonGroup> All { get; } = new[]
    {
        new CommonGroup(Screens, "device-desktop", Left), new CommonGroup(Sound, "volume", Left),
        new CommonGroup(Power, "power", Right), new CommonGroup(SmartDevices, "plug", Right),
    };
}
