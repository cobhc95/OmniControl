namespace OmniControl.App.Features.Automation;

/// What pausing, the "Hotkeys and automation" switch and the safe start stop. Rules of other pages (a feature's own rule action) keep running
/// while rules are paused or the switch is off; the safe start (Shift held at start-up) stops everything, blocks included.
public static class AutomationPause
{
    /// True when the rule may not run now (<paramref name="userRulesOn"/>: the "Hotkeys and automation" feature is switched on).
    public static bool Stops(bool safeStart, bool paused, bool userRulesOn, bool featureRule) => safeStart || ((paused || !userRulesOn) && !featureRule);

    /// True when the block rules are not asked about an action now (<paramref name="featureAction"/>: it is a feature's own rule action).
    public static bool BlocksOff(bool safeStart, bool paused, bool featureAction) => safeStart || (paused && !featureAction);
}
