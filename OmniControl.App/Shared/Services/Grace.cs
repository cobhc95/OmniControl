namespace OmniControl.App.Shared.Services;

/// The one "did that happen less than a grace period ago" test (echo suppression, retry spacing, trust windows); the periods are in <see cref="Timing"/>.
public static class Grace
{
    public static bool Within(DateTime since, TimeSpan period) => DateTime.UtcNow - since < period;
}
