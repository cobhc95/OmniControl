namespace OmniControl.App.Tray;

/// <summary>
/// What the tray flyout and menu hold before the user edits them (docs/mockups/tray-flyout.html). Only ids, written as a saved tray.json
/// would hold them: each is looked up in the live master lists, so an id that is not registered (no smart device, no monitor that reports
/// brightness, a feature that is gone) simply does not show, and nothing here names, draws or runs anything itself.
/// </summary>
public static class TrayDefaults
{
    /// <summary>Brightness, one row per display; then the volume with the default speaker as its pick list.</summary>
    public static IReadOnlyList<TrayElement> Sliders => new[]
    {
        TrayElement.OfState("displays.*.brightness"),
        TrayElement.OfState("volume.master.level", choice: "volume.default.speaker"),
    };

    /// <summary>Effects on/off, every smart device, night light, area screenshot, microphone mute, rotate.</summary>
    public static IReadOnlyList<TrayElement> Tiles => new[]
    {
        TrayElement.OfState("volume.master.fx"),
        TrayElement.OfState("devices.*.power"),
        TrayElement.OfState("displays.warmlight"),
        TrayElement.OfAction("screenshot.area"),
        TrayElement.OfState("volume.mic.muted"),
        TrayElement.OfAction("displays.rotate.portrait"),
    };

    /// <summary>The right-click menu: groups of master actions (a thin line between groups); a toggle names its state, which ticks it.</summary>
    public static IReadOnlyList<IReadOnlyList<TrayElement>> Menu { get; } = new[]
    {
        new[] { TrayElement.OfAction("app.show"), TrayElement.OfAction("app.actions.open") },
        new[] { TrayElement.OfAction("screenshot.area"), TrayElement.OfAction("screenshot.record") },
        new[] { TrayElement.OfAction("automation.pause", state: "automation.paused"), TrayElement.OfAction("volume.fx", state: "volume.master.fx") },
        new[] { TrayElement.OfAction("app.settings.open"), TrayElement.OfAction("app.quit") },
    };
}
