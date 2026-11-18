namespace OmniControl.App.Services;

/// <summary>How often the periodic safety check of <see cref="HardwareWatches"/> looks at the hardware. Stored by number: do not reorder.</summary>
public enum HardwareCheck { Off, EveryMinute, Every5Minutes, Every15Minutes }

/// <summary>The one list of those choices, their wording and their interval: the "Check hardware every" card reads it, and so does the check.</summary>
public static class HardwareChecks
{
    public static readonly IReadOnlyList<(HardwareCheck Value, string Label, TimeSpan? Every)> All = new (HardwareCheck, string, TimeSpan?)[]
    {
        (HardwareCheck.Off, "Off", null),
        (HardwareCheck.EveryMinute, "1 minute", TimeSpan.FromMinutes(1)),
        (HardwareCheck.Every5Minutes, "5 minutes", TimeSpan.FromMinutes(5)),
        (HardwareCheck.Every15Minutes, "15 minutes", TimeSpan.FromMinutes(15)),
    };

    public static string LabelOf(HardwareCheck value) => All.First(e => e.Value == value).Label;

    public static HardwareCheck Find(string label) => All.FirstOrDefault(e => e.Label == label).Value;

    public static TimeSpan? EveryOf(HardwareCheck value) => All.First(e => e.Value == value).Every;
}
