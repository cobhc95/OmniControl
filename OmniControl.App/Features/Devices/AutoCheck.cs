namespace OmniControl.App.Features.Devices;

/// When a device's real state is read on its own. The numbers are what older files stored: 1 = when the
/// page opens, 2 = every 5 min, 3 = every 15 min (those two now also read at start-up).
public enum AutoCheck { Off = 0, WhenOpens = 1, StartAndEvery5 = 2, StartAndEvery15 = 3, StartOnly = 4 }

/// The master list of auto-check choices and the pure rules around them (which choice a device uses, what a pick changes, what is due).
public static class AutoChecks
{
    /// One choice: its words, whether it reads when OmniControl starts, its repeat interval (null = not periodic) and whether it reads when the page or tray panel opens.
    public sealed record Choice(AutoCheck Value, string Name, bool AtStart, TimeSpan? Every, bool WhenShown);

    public static IReadOnlyList<Choice> All { get; } = new[]
    {
        new Choice(AutoCheck.StartAndEvery5, "When OmniControl starts and every 5 min", true, Timing.DeviceAutoCheck5, false),
        new Choice(AutoCheck.StartAndEvery15, "When OmniControl starts and every 15 min", true, Timing.DeviceAutoCheck15, false),
        new Choice(AutoCheck.StartOnly, "When OmniControl starts only", true, null, false),
        new Choice(AutoCheck.WhenOpens, "When the page or tray panel opens", false, null, true),
        new Choice(AutoCheck.Off, "Off (Check now only)", false, null, false),
    };

    /// The choice used until the user picks one.
    public const AutoCheck Default = AutoCheck.StartAndEvery5;

    public const string ManualOnly = "manual only";

    public static Choice Of(AutoCheck value) => All.First(c => c.Value == value);

    /// Checks use Scripts only: a device with no Scripts method is read by hand (Check now) and never on its own.
    public static bool CanAuto(DeviceRecord device) => device.Methods.Any(m => !m.IsSimulation);

    /// The choice a device follows now; null = manual only (simulation-only device).
    public static AutoCheck? Effective(DeviceSettings settings, DeviceRecord device) =>
        !CanAuto(device) ? null : settings.AutoCheckSame ? settings.AutoCheck : device.AutoCheck ?? settings.AutoCheck;

    /// A pick: while "Same for every device" is on it is the one value for all (the devices' own values are dropped); otherwise it is this device's.
    public static void Pick(DeviceSettings settings, DeviceRecord device, AutoCheck value)
    {
        if (settings.AutoCheckSame)
        {
            settings.AutoCheck = value;
            foreach (var each in settings.Devices) each.AutoCheck = null;
        }
        else device.AutoCheck = value;
    }

    /// "Also when the tray panel opens" for a device (Scripts only, so false for a simulation-only device); follows "Same for every device" like the choice.
    public static bool OnTray(DeviceSettings settings, DeviceRecord device) =>
        CanAuto(device) && (settings.AutoCheckSame ? settings.AutoCheckOnTray : device.AutoCheckOnTray ?? settings.AutoCheckOnTray);

    /// The tray panel re-reads this device when it opens: its own switch, or the choice "When the page or tray panel opens".
    public static bool ReadsWhenTrayOpens(DeviceSettings settings, DeviceRecord device) =>
        OnTray(settings, device) || (Effective(settings, device) is { } choice && Of(choice).WhenShown);

    /// A pick of the tray switch: shared or this device's, as <see cref="Pick"/>.
    public static void PickOnTray(DeviceSettings settings, DeviceRecord device, bool value)
    {
        if (settings.AutoCheckSame)
        {
            settings.AutoCheckOnTray = value;
            foreach (var each in settings.Devices) each.AutoCheckOnTray = null;
        }
        else device.AutoCheckOnTray = value;
    }

    /// The switch: turning it on makes the device's current choice the one for all; turning it off lets each device keep that same value to edit on its own.
    public static void SetSame(DeviceSettings settings, DeviceRecord device, bool same)
    {
        if (settings.AutoCheckSame == same) return;
        var current = Effective(settings, device) ?? settings.AutoCheck;
        var onTray = device.AutoCheckOnTray ?? settings.AutoCheckOnTray;
        settings.AutoCheckSame = same;
        if (same)
        {
            settings.AutoCheck = current;
            settings.AutoCheckOnTray = onTray;
            foreach (var each in settings.Devices)
            {
                each.AutoCheck = null;
                each.AutoCheckOnTray = null;
            }
        }
    }

    /// The periodic check is due: never read yet by the timer, or the interval has passed.
    public static bool Due(DateTime? lastChecked, DateTime now, TimeSpan every) => lastChecked is not { } last || now - last >= every;
}
