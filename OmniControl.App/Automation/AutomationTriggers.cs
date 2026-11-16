using OmniControl.App.Hotkeys;
using OmniControl.App.Mouse;
using OmniControl.App.Rules;

namespace OmniControl.App.Automation;

/// <summary>
/// The master list of what starts a "Do" rule. One registration per trigger: the picker, the sentence, the scheduler and the next-run
/// text all read it. Stored as "login", "time|h=7;m=30", "timer|min=10", "program.start:chrome", "hotkey"...
/// </summary>
public static class AutomationTriggers
{
    private const string Startup = "Start up", Clock = "Time and idle", Power = "Power", Programs = "Programs", Devices = "Devices", Keys = "Keyboard";
    public const string Login = "login", Start = "start", Time = "time", Timer = "timer", Idle = "idle", Resume = "resume",
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

    /// <summary>The picker's list. <paramref name="programs"/> are the names to offer for "a program starts or closes".</summary>
    public static IReadOnlyList<PickChoice> Choices(IReadOnlyList<string> programs)
    {
        var list = new List<PickChoice>
        {
            new(Login, Startup, "At log in", Keywords: "startup sign in boot windows start with windows"),
            new(Start, Startup, "When OmniControl starts", Keywords: "launch open app start"),
            new(Time, Clock, "At a time of day", Keywords: "schedule scheduler task clock alarm days weekly daily wake",
                Options: new[] { Hour, Minute }.Concat(AutomationDays.Options).Append(Wake).ToList(), AskFirst: true),
            new(Timer, Clock, "After a timer", Keywords: "schedule scheduler task countdown delay every minutes", Options: new[] { Minutes, Repeat }, AskFirst: true),
            new(Idle, Clock, "When I have been idle", Keywords: "away inactive no input screen saver", Options: new[] { IdleMinutes }, AskFirst: true),
            new(Resume, Power, "When the PC wakes from sleep", Keywords: "resume wake hibernate"),
            new(OnBattery, Power, "When it starts running on battery", Keywords: "unplugged laptop"),
            new(PluggedIn, Power, "When it is plugged in", Keywords: "charging mains ac laptop"),
        };
        foreach (var p in programs)
        {
            list.Add(new PickChoice($"{ProgramStart}:{p}", Programs, $"{p} starts", Keywords: "app program open launch"));
            list.Add(new PickChoice($"{ProgramClose}:{p}", Programs, $"{p} closes", Keywords: "app program quit exit"));
        }
        list.Add(new PickChoice($"{ProgramStart}:", Programs, "Another program starts", "notepad", "app program open launch type name", ParamLabel: ProgramLabel));
        list.Add(new PickChoice($"{ProgramClose}:", Programs, "Another program closes", "notepad", "app program quit exit type name", ParamLabel: ProgramLabel));
        list.Add(new PickChoice($"{DeviceConnect}:", Devices, "A USB device connects", "(any device)", "plug usb headset keyboard drive", ParamLabel: DeviceLabel));
        list.Add(new PickChoice($"{DeviceDisconnect}:", Devices, "A USB device disconnects", "(any device)", "unplug usb headset keyboard drive", ParamLabel: DeviceLabel));
        list.Add(new PickChoice(Hotkey, Keys, "I press a hotkey", Keywords: "shortcut keys keyboard"));
        return list;
    }

    /// <summary>How a stored trigger reads in the sentence. <paramref name="hotkey"/> is the shortcut of the rule's own action (empty: none yet).</summary>
    public static string PhraseOf(string stored)
    {
        var trigger = MouseAction.Parse(stored);
        var o = trigger.Options;
        return trigger.Id switch
        {
            Login => "at log in",
            Start => "when OmniControl starts",
            Time => $"at {o.Int(Hour):00}:{o.Int(Minute):00} {AutomationDays.PhraseOf(o)}",
            Timer => o.Bool(Repeat) ? $"every {o.Int(Minutes)} min" : $"{o.Int(Minutes)} min after it starts",
            Idle => $"when idle for {o.Int(IdleMinutes)} min",
            Resume => "when the PC wakes from sleep",
            OnBattery => "when it starts running on battery",
            PluggedIn => "when it is plugged in",
            ProgramStart => $"when {ProgramNames.Normalize(trigger.Param)} starts",
            ProgramClose => $"when {ProgramNames.Normalize(trigger.Param)} closes",
            DeviceConnect => $"when {DeviceName(trigger.Param)} connects",
            DeviceDisconnect => $"when {DeviceName(trigger.Param)} disconnects",
            Hotkey => "when I press a hotkey",
            _ => "(unknown trigger)",
        };
    }

    /// <summary>True for the entries whose text may stay empty (any device).</summary>
    public static bool AllowsEmptyText(string spec) => spec == DeviceConnect + ":" || spec == DeviceDisconnect + ":";

    /// <summary>True for the trigger that is the rule's own shortcut.</summary>
    public static bool IsHotkey(string stored) => MouseAction.Parse(stored).Id == Hotkey;

    /// <summary>The next time a time-of-day trigger is due after <paramref name="after"/>, or null for any other trigger.</summary>
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

    /// <summary>True when a time-of-day trigger's scheduled moment lies in (<paramref name="from"/>, <paramref name="to"/>] and is not older than the catch-up time.</summary>
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

    /// <summary>True when the time-of-day trigger asks to wake the PC.</summary>
    public static bool WakesPc(string stored) => MouseAction.Parse(stored) is { Id: Time } trigger && trigger.Options.Bool(Wake);

    private static string DeviceName(string param) => param.Trim().Length == 0 ? "a USB device" : param.Trim();
}
