namespace OmniControl.App.Shared.Services;

/// How often the periodic safety check of <see cref="HardwareWatches"/> looks at the hardware. Stored by number: do not reorder.
public enum HardwareCheck { Off, EveryMinute, Every5Minutes, Every15Minutes }

/// The one list of those choices, their wording and their interval: the "Check hardware every" card reads it, and so does the check.
public static class HardwareChecks
{
    public static readonly IReadOnlyList<(HardwareCheck Value, string Label, TimeSpan? Every)> All = new (HardwareCheck, string, TimeSpan?)[]
    {
        (HardwareCheck.Off, "Off", null),
        (HardwareCheck.EveryMinute, "1 minute", Timing.HardwareCheckMinute),
        (HardwareCheck.Every5Minutes, "5 minutes", Timing.HardwareCheck5Minutes),
        (HardwareCheck.Every15Minutes, "15 minutes", Timing.HardwareCheck15Minutes),
    };

    public static IReadOnlyList<string> Labels { get; } = All.Select(e => e.Label).ToList();

    public static string LabelOf(HardwareCheck value) => All.First(e => e.Value == value).Label;

    public static HardwareCheck Find(string label) => All.FirstOrDefault(e => e.Label == label).Value;

    public static TimeSpan? EveryOf(HardwareCheck value) => All.First(e => e.Value == value).Every;
}
