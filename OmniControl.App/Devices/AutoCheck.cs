using OmniControl.App.Services;

namespace OmniControl.App.Devices;

/// <summary>When a device's real state is read on its own (docs/mockups/device-state-refresh.html).</summary>
public enum AutoCheck { Off, WhenOpens, Every5Minutes, Every15Minutes }

/// <summary>The master list of auto-check choices: the chip's words and the repeat interval (null = not periodic). A new choice is one more entry here.</summary>
public static class AutoChecks
{
    public sealed record Choice(AutoCheck Value, string Name, TimeSpan? Every);

    public static IReadOnlyList<Choice> All { get; } = new[]
    {
        new Choice(AutoCheck.Off, "Off", null),
        new Choice(AutoCheck.WhenOpens, "When page opens", null),
        new Choice(AutoCheck.Every5Minutes, "Every 5 min", Timing.DeviceAutoCheck5),
        new Choice(AutoCheck.Every15Minutes, "Every 15 min", Timing.DeviceAutoCheck15),
    };

    /// <summary>The choice used until the user picks one: scripts read when the page opens; simulation is off because a periodic check keeps the emulator running.</summary>
    public static AutoCheck DefaultFor(string method) => method == DeviceMethods.Simulation ? AutoCheck.Off : AutoCheck.WhenOpens;

    public static Choice Of(AutoCheck value) => All.First(c => c.Value == value);
}
