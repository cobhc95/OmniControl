using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Mouse;

/// <summary>Every option a mouse action can offer, defined once. The catalog attaches them to actions; the runner and modes read them.</summary>
public static class MouseOptions
{
    // Common to actions that happen once per press: when they run, how many times, and how fast they repeat while held.
    public const string OnPress = "When the button is pressed", OnRelease = "When the button is released",
        Repeatedly = "Repeatedly while the button is held", KeysHeld = "Keys held down while the button is held";
    public static readonly ActionOption Trigger = ActionOption.Choice("trigger", "Runs", new[] { OnPress, OnRelease, Repeatedly });
    public static readonly ActionOption TriggerWithHold = ActionOption.Choice("trigger", "Runs", new[] { OnPress, OnRelease, Repeatedly, KeysHeld });
    public static readonly ActionOption Times = ActionOption.Number("times", "Times each time", 1, 1, 20).ShownWhen(Trigger, OnPress, OnRelease, Repeatedly);
    public static readonly ActionOption RepeatDelay = ActionOption.Number("delay", "Wait before repeating", 500, 100, 2000, 50, " ms").ShownWhen(Trigger, Repeatedly);
    public static readonly ActionOption RepeatEvery = ActionOption.Number("every", "Repeat every", 100, 20, 1000, 10, " ms").ShownWhen(Trigger, Repeatedly);
    // Opt-in extras for almost every action: let the input's own action through (a button's click, Windows' own gesture), and/or send a shortcut with it.
    // One setting ("original"), worded by the kind of input: a trackpad gesture is never swallowed, so for it this means "do not turn Windows' gesture off".
    public static readonly ActionOption KeepOriginal = ActionOption.Toggle("original", "Also send the button's normal click");
    // The one switch of "Block apps' middle-click autoscroll" as an action's row (ON = let the app's autoscroll start = not blocked). It is a
    // global setting kept once in MouseSettings (MouseService is its ISharedOptions), so every button using the action shows the same value.
    public static readonly ActionOption AllowAutoscroll = ActionOption.Toggle("autoscroll", "Let the app's own autoscroll start").SharedSetting()
        .NeedsOn(KeepOriginal, "Only matters while the button's normal click is also sent.");
    public static readonly ActionOption KeepWindowsGesture = KeepOriginal with { Label = "Also let Windows do its own gesture" };
    public static readonly ActionOption AlsoSend = ActionOption.Text("also", "Also send this shortcut", "", "Ctrl+C");

    /// <summary>The "also keep the original" setting as it reads for this kind of input.</summary>
    public static ActionOption OriginalFor(bool trackpadGesture) => trackpadGesture ? KeepWindowsGesture : KeepOriginal;

    public static readonly IReadOnlyList<ActionOption> Common = new[] { Trigger, Times, RepeatDelay, RepeatEvery };

    // What only the first of an input's actions decides: when it runs (pressed, released, repeated, keys held) and whether the input
    // itself is let through. The second and later actions run once, in order, each time the input fires.
    private static readonly HashSet<string> FirstActionOnly = new() { Trigger.Key, RepeatDelay.Key, RepeatEvery.Key, KeepOriginal.Key };

    /// <summary>
    /// The settings an action keeps as an input's second or later action: its own, without <see cref="FirstActionOnly"/>; "times" is
    /// then always shown (it no longer depends on the trigger). Null when nothing is left.
    /// </summary>
    public static IReadOnlyList<ActionOption>? OneShot(IReadOnlyList<ActionOption>? options) =>
        options?.Where(o => !FirstActionOnly.Contains(o.Key))
            .Select(o => o.ShowKey is { } key && FirstActionOnly.Contains(key) ? o with { ShowKey = null, ShowValues = null } : o)
            .ToList() is { Count: > 0 } left ? left : null;

    // Window actions.
    private const string UnderPointer = "Window under the pointer";
    public const string ActiveWindow = "Active window";
    public static readonly ActionOption Target = ActionOption.Choice("target", "Act on", new[] { UnderPointer, ActiveWindow });

    // Layers.
    public static readonly ActionOption Wrap = ActionOption.Toggle("wrap", "Wrap around at the ends", on: true);

    // Web search of the selected text.
    public const string CustomEngine = "Custom address";
    public static readonly ActionOption Engine = ActionOption.Choice("engine", "Search engine", new[] { "Google", "Bing", "DuckDuckGo", CustomEngine });
    public static readonly ActionOption CustomAddress = ActionOption.Text("custom", "Custom address (the text is added at the end)", "", "https://example.com/search?q=").ShownWhen(Engine, CustomEngine);

    // Run a program.
    public static readonly ActionOption Arguments = ActionOption.Text("args", "Arguments");
    public static readonly ActionOption StartIn = ActionOption.Path("startin", "Start in folder", PathKind.Folder);
    // An app's "Open ..." action ("Open Spotify"): the program to start instead of the one found.
    public static readonly ActionOption AppProgram = ActionOption.Path("path", "Program (empty: the one found)", PathKind.Program);
    public static readonly ActionOption WindowStyle = ActionOption.Choice("style", "Window", new[] { "Normal", "Minimised", "Maximised", "Hidden" });
    public static readonly ActionOption Administrator = ActionOption.Toggle("admin", "Run as administrator");

    /// <summary>The settings of an action that starts a program the catalogue found (<see cref="HotkeyAction.RunLaunch"/>): its own choice of program, arguments and folder.</summary>
    public static readonly IReadOnlyList<ActionOption> Launching = new[] { AppProgram, Arguments, StartIn };

    /// <summary>What an input chose for a program-starting action.</summary>
    public static LaunchSettings LaunchOf(OptionValues values) => new(values.Text(AppProgram), values.Text(Arguments), values.Text(StartIn));

    // How far a level action (volume, brightness...) moves per press.
    public static readonly ActionOption Step = ActionOption.Number("step", "Change by", HotkeyAction.DefaultStep, 1, 100, 1, " %");

    // A power action's cancellable countdown, and whether shutting down closes programs that object.
    public static readonly ActionOption Countdown = ActionOption.Number("countdown", "Countdown before it happens", (int)Timing.PowerCountdown.TotalSeconds,
        Timing.PowerCountdownMinSeconds, Timing.PowerCountdownMaxSeconds, 5, " s");
    public static readonly ActionOption ForceClose = ActionOption.Toggle("force", "Close programs that don't respond (unsaved work is lost)");

    /// <summary>What an input chose for an OmniControl action: its program, how far it moves a level, its power countdown (each used only by the actions that take it).</summary>
    public static RunSettings RunOf(OptionValues values) =>
        new(LaunchOf(values), values.Int(Step), values.Int(Countdown), values.Bool(ForceClose), values);

    // Repeated clicking (the master action "Repeated clicking"): one list, read by the picker and by RepeatedClicking.
    public const string StartToggle = "Press to toggle", StartHeld = "While held",
        StopNever = "Until stopped", StopClicks = "After a number of clicks", StopSeconds = "After a number of seconds",
        AtPointer = "At the pointer", AtPoint = "At a fixed point", TypeSingle = "Single", TypeDouble = "Double", TypeHold = "Hold down";
    public static readonly ActionOption ClickButton = ActionOption.Choice("rcbutton", "Button", new[] { "Left", "Right", "Middle", "Back", "Forward" });
    public static readonly ActionOption ClickType = ActionOption.Choice("rctype", "Click type", new[] { TypeSingle, TypeDouble, TypeHold });
    public static readonly ActionOption ClickStart = ActionOption.Choice("rcstart", "Start and stop", new[] { StartToggle, StartHeld });
    public static readonly ActionOption ClickStop = ActionOption.Choice("rcstop", "Stop after", new[] { StopNever, StopClicks, StopSeconds });
    public static readonly ActionOption ClickCount = ActionOption.Number("rccount", "Number of clicks", 100, 1, 1000000).ShownWhen(ClickStop, StopClicks);
    public static readonly ActionOption ClickSeconds = ActionOption.Number("rcseconds", "Number of seconds", 10, 1, 86400, 1, " s").ShownWhen(ClickStop, StopSeconds);
    public static readonly ActionOption ClickSpeed = ActionOption.Number("rcspeed", "Speed", 10, 1, Timing.ClickSpeedMax, 1, " clicks/s");
    public static readonly ActionOption ClickWhere = ActionOption.Choice("rcwhere", "Where", new[] { AtPointer, AtPoint });
    public static readonly ActionOption ClickPoint = ActionOption.Point("rcpoint", "The point").ShownWhen(ClickWhere, AtPoint);
    public static readonly ActionOption ClickJitter = ActionOption.Number("rcjitter", "Random timing", 10, 0, 50, 5, " %");
    public static readonly ActionOption ClickStopOnMove = ActionOption.Toggle("rcmove", "Stop when the mouse moves");
    public static readonly ActionOption ClickBadge = ActionOption.Toggle("rcbadge", "Show on-screen badge", on: true);
    public static readonly IReadOnlyList<ActionOption> RepeatClicking = new[]
    {
        ClickButton, ClickType, ClickStart, ClickStop, ClickCount, ClickSeconds, ClickSpeed, ClickWhere, ClickPoint, ClickJitter, ClickStopOnMove, ClickBadge,
        ActionOption.Heading("rcnote", "Esc always stops it.", $"Never faster than {Timing.ClickSpeedMax} clicks/s."),
    };

    // Pointer speed.
    public static readonly ActionOption SlowSpeed = ActionOption.Number("speed", "Slow speed (1 slowest, 20 fastest)", 3, 1, 20);
    public static readonly ActionOption CycleSpeeds = ActionOption.Text("speeds", "Speeds to cycle through (1-20, separated by commas)", "4,8,12,16,20");

    // Scrolling.
    public static readonly ActionOption ByPages = ActionOption.Toggle("pages", "Scroll by pages instead of lines");
    public const string Both = "Vertical and sideways", VerticalOnly = "Vertical only", SidewaysOnly = "Sideways only";
    public static readonly ActionOption ScrollDistance = ActionOption.Number("distance", "Pointer distance per scroll step", 20, 2, 200, 2, " px");
    public static readonly ActionOption ScrollAmount = ActionOption.Number("notches", "Scroll amount per step", 1, 1, 10);
    public static readonly ActionOption ScrollDirection = ActionOption.Choice("direction", "Scroll", new[] { Both, VerticalOnly, SidewaysOnly });
    public static readonly ActionOption ReverseVertical = ActionOption.Toggle("reversev", "Reverse the vertical direction").ShownWhen(ScrollDirection, Both, VerticalOnly);
    public static readonly ActionOption ReverseSideways = ActionOption.Toggle("reverseh", "Reverse the sideways direction").ShownWhen(ScrollDirection, Both, SidewaysOnly);
    public static readonly ActionOption ScrollSticky = ActionOption.Toggle("sticky", "Keep scrolling until the button is pressed again");
    // The pointer while an action that lasts is running: one of Windows' pointers, shown everywhere.
    public const string PointerSame = "Keep my pointer", PointerFourArrows = "Four arrows",
        PointerUpDown = "Up and down arrows", PointerLeftRight = "Left and right arrows", PointerHand = "Hand", PointerCrosshair = "Crosshair";
    private static readonly string[] PointerChoices = { PointerFourArrows, PointerSame, PointerUpDown, PointerLeftRight, PointerHand, PointerCrosshair };

    /// <summary>Movement as scroll shows the four-arrow pointer unless told otherwise.</summary>
    public static readonly ActionOption ScrollPointer = ActionOption.Choice("look", "Pointer while scrolling", PointerChoices);

    /// <summary>The other held actions leave the pointer alone unless told otherwise.</summary>
    public static readonly ActionOption HeldPointer = ActionOption.Choice("look", "Pointer while held", PointerChoices.OrderBy(c => c == PointerSame ? 0 : 1).ToArray());

    /// <summary>The Windows pointer a choice stands for, or null for keeping the user's pointer.</summary>
    public static PointerShape.Shape? ShapeOf(string choice) => choice switch
    {
        PointerFourArrows => PointerShape.Shape.FourArrows,
        PointerUpDown => PointerShape.Shape.UpDown,
        PointerLeftRight => PointerShape.Shape.LeftRight,
        PointerHand => PointerShape.Shape.Hand,
        PointerCrosshair => PointerShape.Shape.Crosshair,
        _ => null,
    };

    // Sticky buttons.
    public static readonly ActionOption ReleaseAfter = ActionOption.Number("release", "Let go automatically after (0 = never)", 0, 0, 120, 1, " s");
}
