namespace OmniControl.App.States;

/// <summary>
/// THE list of the cards of the basic set another PC sees (the Other PCs page): each is a name and an icon, and an owner puts its state or
/// action in one by setting <c>Common</c> to the name on its master entry. Everything flagged is shared with every paired PC; nothing else is.
/// </summary>
public static class CommonGroups
{
    public const string Screens = "Screens", Sound = "Sound", NowPlaying = "Now playing", SmartDevices = "Smart devices", Power = "Power";

    /// <summary>The cards in the order they are shown, with the library icon each header takes.</summary>
    public static IReadOnlyList<(string Name, string Icon)> All { get; } = new[]
    {
        (Screens, "device-desktop"), (Sound, "volume"), (NowPlaying, "music"), (SmartDevices, "plug"), (Power, "power"),
    };
}
