namespace OmniControl.App.Features.Tray;

/// What the tray flyout and menu hold before the user edits them. Only ids, written as a saved tray.json
/// would hold them: each is looked up in the live master lists, so an id that is not registered (no smart device, no monitor that reports
/// brightness, a feature that is gone) simply does not show, and nothing here names, draws or runs anything itself.
public static class TrayDefaults
{
    /// Brightness, one row with a chip choosing a display or all of them; then the volume with the default speaker as its pick list.
    public static IReadOnlyList<TrayElement> Sliders => new[]
    {
        new TrayElement { State = "displays.*.brightness", Together = true },
        new TrayElement { State = "volume.master.level", Choice = "volume.default.speaker", AllState = "volume.speakers.all.level" },
    };

    /// Effects on/off, every smart device, night light, area screenshot, microphone mute, rotate.
    public static IReadOnlyList<TrayElement> Tiles => new[]
    {
        TrayElement.OfState("volume.master.fx"),
        TrayElement.OfState("devices.*.power"),
        TrayElement.OfState("displays.warmlight"),
        TrayElement.OfAction("screenshot.area"),
        TrayElement.OfState("volume.mic.muted"),
        TrayElement.OfAction("displays.rotate.portrait"),
        TrayElement.OfAction("displays.screens.off"),
    };

    /// The right-click menu: groups of master actions (a thin line between groups); a toggle names its state, which ticks it.
    public static IReadOnlyList<IReadOnlyList<TrayElement>> Menu { get; } = new[]
    {
        new[] { TrayElement.OfAction("app.show"), TrayElement.OfAction("app.actions.open") },
        new[] { TrayElement.OfAction("automation.pause", state: "automation.paused") },
        new[] { TrayElement.OfAction("app.settings.open"), TrayElement.OfAction("app.quit") },
    };
}
