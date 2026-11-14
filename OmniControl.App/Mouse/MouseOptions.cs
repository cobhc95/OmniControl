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
    public static readonly ActionOption StartIn = ActionOption.Text("startin", "Start in folder");
    public static readonly ActionOption WindowStyle = ActionOption.Choice("style", "Window", new[] { "Normal", "Minimised", "Maximised", "Hidden" });
    public static readonly ActionOption Administrator = ActionOption.Toggle("admin", "Run as administrator");

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
