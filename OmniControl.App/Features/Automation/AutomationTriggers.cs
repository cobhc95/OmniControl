using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Shared.Scripts;

namespace OmniControl.App.Features.Automation;

/// The master list of what starts a "Do" rule. The fixed triggers are ONE table (<see cref="Table"/>: id, section, name, keywords, the
/// phrase it reads as, its settings); the picker, the sentence, the scheduler and the next-run text all read it. The live families come
/// from other master lists, read each time (<c>AutomationTriggers.Live.cs</c>): every state of <c>StateRegistry</c> and every action of
/// <see cref="HotkeyRegistry"/>. Stored as "login", "time|h=7;m=30", "timer|min=10", "program.start:chrome", "state:id|when=turns on",
/// "action.runs:id", "hotkey"...
public static partial class AutomationTriggers
{
    private const string Startup = "Start up", Clock = "Time and idle", Power = "Power", Programs = "Programs", Devices = "Devices", Keys = "Keyboard";
    public const string Login = "login", Start = "start", Time = "time", Between = AutomationConditions.Between, Timer = "timer", Idle = "idle", Resume = "resume",
        OnBattery = "battery", PluggedIn = "plugged", ProgramStart = "program.start", ProgramClose = "program.close",
        DeviceConnect = "device.connect", DeviceDisconnect = "device.disconnect", Hotkey = "hotkey";

    public static readonly ActionOption Hour = ActionOption.Number("h", "Hour", 7, 0, 23);
    public static readonly ActionOption Minute = ActionOption.Number("m", "Minute", 0, 0, 59);
    public static readonly ActionOption Wake = ActionOption.Toggle("wake", "Wake the PC from sleep for this");
    public static readonly ActionOption Minutes = ActionOption.Number("min", "After", 10, 1, 1440, 1, " min");
    public static readonly ActionOption Repeat = ActionOption.Toggle("repeat", "Repeat");
    public static readonly ActionOption IdleMinutes = ActionOption.Number("min", "Idle for", 10, 1, 1440, 1, " min");

    private const string ProgramLabel = "Program name, as Task Manager shows it";
    private const string DeviceLabel = "Part of the device's name (empty: any device)";

    /// One fixed trigger. <see cref="ParamHint"/>: it takes a typed text (a program, a device). <see cref="PerProgram"/>: the picker also
    /// lists it once per known program, named by this.
    private sealed record Fixed(string Id, string Section, string Name, string Keywords, Func<MouseAction, string> Phrase,
        IReadOnlyList<ActionOption>? Options = null, string? ParamHint = null, string ParamLabel = "", Func<string, string>? PerProgram = null);

    /// THE table of fixed triggers, in picker order.
    private static readonly Fixed[] Table =
    {
        new(Login, Startup, "At log in", "startup sign in boot windows start with windows", _ => "at log in"),
        new(Start, Startup, "When OmniControl starts", "launch open app start", _ => "when OmniControl starts"),
        new(Time, Clock, "At a time of day", "schedule scheduler task clock alarm days weekly daily wake",
            t => $"at {t.Options.Int(Hour):00}:{t.Options.Int(Minute):00} {AutomationDays.PhraseOf(t.Options)}",
            new[] { Hour, Minute }.Concat(AutomationDays.Options).Append(Wake).ToList()),
        new(Between, Clock, "Between two times", "night hours clock schedule window while",
            t => AutomationConditions.WindowOf(t.Options),
            AutomationConditions.WindowOptions),
        new(Timer, Clock, "After a timer", "schedule scheduler task countdown delay every minutes",
            t => t.Options.Bool(Repeat) ? $"every {t.Options.Int(Minutes)} minutes" : $"{t.Options.Int(Minutes)} min after it starts", new[] { Minutes, Repeat }),
        new(Idle, Clock, "When I have been idle", "away inactive no input screen saver", t => $"when idle for {t.Options.Int(IdleMinutes)} min", new[] { IdleMinutes }),
        new(Resume, Power, "When the PC wakes from sleep", "resume wake hibernate", _ => "when the PC wakes from sleep"),
        new(OnBattery, Power, "When it starts running on battery", "unplugged laptop", _ => "when it starts running on battery"),
        new(PluggedIn, Power, "When it is plugged in", "charging mains ac laptop", _ => "when it is plugged in"),
        new(ProgramStart, Programs, "Another program starts", "app program open launch type name", t => $"when {ProgramNames.Normalize(t.Param)} starts",
            ParamHint: "notepad", ParamLabel: ProgramLabel, PerProgram: p => $"{p} starts"),
        new(ProgramClose, Programs, "Another program closes", "app program quit exit type name", t => $"when {ProgramNames.Normalize(t.Param)} closes",
            ParamHint: "notepad", ParamLabel: ProgramLabel, PerProgram: p => $"{p} closes"),
        new(DeviceConnect, Devices, "A USB device connects", "plug usb headset keyboard drive", t => $"when {DeviceName(t.Param)} connects",
            ParamHint: "(any device)", ParamLabel: DeviceLabel),
        new(DeviceDisconnect, Devices, "A USB device disconnects", "unplug usb headset keyboard drive", t => $"when {DeviceName(t.Param)} disconnects",
            ParamHint: "(any device)", ParamLabel: DeviceLabel),
        new(Hotkey, Keys, "I press a hotkey", "shortcut keys keyboard", _ => "when I press a hotkey"),
    };

    /// The fixed part of the picker's list. <paramref name="programs"/> are the names to offer for "a program starts or closes".
    private static IEnumerable<PickChoice> FixedChoices(IReadOnlyList<string> programs)
    {
        var perProgram = Table.Where(t => t.PerProgram is not null).ToList();
        foreach (var t in Table)
        {
            if (t == perProgram.FirstOrDefault()) // the known programs come first in their section, each with its start and close
                foreach (var p in programs)
                foreach (var each in perProgram)
                    yield return new PickChoice($"{each.Id}:{p}", each.Section, each.PerProgram!(p), Keywords: each.Keywords);
            yield return t.ParamHint is null
                ? new PickChoice(t.Id, t.Section, t.Name, Keywords: t.Keywords, Options: t.Options, AskFirst: t.Options is { Count: > 0 })
                : new PickChoice($"{t.Id}:", t.Section, t.Name, t.ParamHint, t.Keywords, ParamLabel: t.ParamLabel);
        }
    }

    /// The names of the fixed triggers, for search.
    public static IReadOnlyList<string> FixedNames { get; } = Table.Select(t => t.Name).ToList();

    /// How a fixed trigger reads in the sentence; null when the stored text is not one.
    private static string? FixedPhraseOf(MouseAction trigger) => Table.FirstOrDefault(t => t.Id == trigger.Id)?.Phrase(trigger);

    /// True for the entries whose text may stay empty (any device).
    public static bool AllowsEmptyText(string spec) => spec == DeviceConnect + ":" || spec == DeviceDisconnect + ":";

    /// True for the trigger that is the rule's own shortcut.
    public static bool IsHotkey(string stored) => MouseAction.Parse(stored).Id == Hotkey;

    /// The next time a time-of-day trigger is due after <paramref name="after"/>, or null for any other trigger.
    public static DateTime? NextTime(string stored, DateTime after)
    {
        var trigger = MouseAction.Parse(stored);
        if (trigger.Id != Time) return null;
        for (var day = 0; day <= 7; day++)
        {
            var at = after.Date.AddDays(day).AddHours(trigger.Options.Int(Hour)).AddMinutes(trigger.Options.Int(Minute));
            if (at > after && AutomationDays.Includes(trigger.Options, at.DayOfWeek)) return at;
        }
        return null;
    }

    /// True when a time-of-day trigger's scheduled moment lies in (<paramref name="from"/>, <paramref name="to"/>] and is not older than the catch-up time.
    public static bool TimeIsDue(string stored, DateTime from, DateTime to, TimeSpan catchUp)
    {
        var trigger = MouseAction.Parse(stored);
        if (trigger.Id != Time) return false;
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
        {
            var at = date.AddHours(trigger.Options.Int(Hour)).AddMinutes(trigger.Options.Int(Minute));
            if (at > from && at <= to && to - at <= catchUp && AutomationDays.Includes(trigger.Options, at.DayOfWeek)) return true;
        }
        return false;
    }

    /// True when a "between two times" trigger's window began in (<paramref name="from"/>, <paramref name="to"/>].
    public static bool WindowBegins(string stored, DateTime from, DateTime to)
    {
        var trigger = MouseAction.Parse(stored);
        if (trigger.Id != Between) return false;
        var start = AutomationConditions.MinutesOf(trigger.Options).From;
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
            if (date.AddMinutes(start) is var at && at > from && at <= to) return true;
        return false;
    }

    /// True for a trigger that can hold over a while (a state, a time window), not only happen at a moment.
    public static bool CanHold(string stored) => MouseAction.Parse(stored).Id is StateChanges or Between;

    /// True when the time-of-day trigger asks to wake the PC.
    public static bool WakesPc(string stored) => MouseAction.Parse(stored) is { Id: Time } trigger && trigger.Options.Bool(Wake);

    private static string DeviceName(string param) => param.Trim().Length == 0 ? "a USB device" : param.Trim();
}
