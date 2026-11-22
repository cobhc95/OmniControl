namespace OmniControl.App.Shared.Services;

/// How long the closed window stays ready before its memory is freed. Stored by number: do not reorder.
public enum UiRelease { After30Seconds, After1Minute, After2Minutes, After5Minutes, Never, Always }

/// The one list of those choices, their wording and their wait: the "Free memory after closing" card reads it, and so does the shell's release timer.
public static class UiReleases
{
    public static readonly IReadOnlyList<(UiRelease Value, string Label, TimeSpan? After)> All = new (UiRelease, string, TimeSpan?)[]
    {
        (UiRelease.Always, "Always", Timing.UiReleaseAlways),
        (UiRelease.After30Seconds, "30 seconds", Timing.UiReleaseAfterHidden),
        (UiRelease.After1Minute, "1 minute", Timing.UiRelease1Minute),
        (UiRelease.After2Minutes, "2 minutes", Timing.UiRelease2Minutes),
        (UiRelease.After5Minutes, "5 minutes", Timing.UiRelease5Minutes),
        (UiRelease.Never, "Never", null),
    };

    public static string LabelOf(UiRelease value) => All.First(e => e.Value == value).Label;

    public static UiRelease Find(string label) => All.FirstOrDefault(e => e.Label == label).Value;

    public static TimeSpan? AfterOf(UiRelease value) => All.First(e => e.Value == value).After;
}
