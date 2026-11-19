using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.States;

/// One card of the basic set: its name, its library icon and the page column it always sits in.
public sealed record CommonGroup(string Name, string Icon, int Column) : IColumnPlaced;

/// THE list of the cards of the basic set another PC sees (the Other PCs page): each is a name, an icon and a fixed column, and an owner puts its state or
/// action in one by setting <c>Common</c> to the name on its master entry. Everything flagged is shared with every paired PC; nothing else is.
/// The list's order is the reading order of the one-column (narrow) layout and the order inside each column of the wide one
///, so a group never moves when the connection refreshes.
public static class CommonGroups
{
    public const string Screens = "Screens", Sound = "Sound", SmartDevices = "Smart devices", Power = "Power";

    /// Left column: what you see and hear. Right column: the PC and the room.
    private const int Left = 0, Right = 1;

    public static IReadOnlyList<CommonGroup> All { get; } = new[]
    {
        new CommonGroup(Screens, "device-desktop", Left), new CommonGroup(Sound, "volume", Left),
        new CommonGroup(Power, "power", Right), new CommonGroup(SmartDevices, "plug", Right),
    };
}
