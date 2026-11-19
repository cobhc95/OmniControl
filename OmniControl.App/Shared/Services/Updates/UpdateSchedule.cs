namespace OmniControl.App.Shared.Services.Updates;

/// When the daily update check is due (pure).
public static class UpdateSchedule
{
    /// True when the switch is on and no check ran within <paramref name="every"/> (a clock set back counts as due).
    public static bool IsDue(bool daily, DateTime? lastCheck, DateTime now, TimeSpan every) =>
        daily && (lastCheck is not { } last || now < last || now - last >= every);
}
