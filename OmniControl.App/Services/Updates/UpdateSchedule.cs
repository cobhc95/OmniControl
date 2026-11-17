namespace OmniControl.App.Services.Updates;

/// <summary>When the daily update check is due (pure).</summary>
public static class UpdateSchedule
{
    /// <summary>True when the switch is on and no check ran within <paramref name="every"/> (a clock set back counts as due).</summary>
    public static bool IsDue(bool daily, DateTime? lastCheck, DateTime now, TimeSpan every) =>
        daily && (lastCheck is not { } last || now < last || now - last >= every);
}
