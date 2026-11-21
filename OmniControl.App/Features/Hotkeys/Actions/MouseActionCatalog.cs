using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;

namespace OmniControl.App.Features.Hotkeys.Actions;

/// How a layer action moves its family's layer.
public enum LayerMove { Next, Previous, Reset, Switch, Hold }

/// A layer action read from its id: whose layer it moves (mouse or trackpad), how, and to which layer (Switch and Hold).
public sealed record LayerAction(InputFamily Family, LayerMove Move, int Layer = 0);

/// Every action a mouse button can be given, in one table. An action is stored as text: "id", or "id:parameter" for
/// "keys:Ctrl+Shift+T", "run:C:\app.exe", "folder:C:\Docs" and "omni:&lt;hotkey id&gt;" (any OmniControl hotkey action).
/// Layer actions are "layer.next", "layer.previous", "layer.reset", "layer.set.N" and "layer.hold.N" for the mouse's layers, and the same
/// with "trackpad." in front for the trackpad's.
public static class MouseActionCatalog
{
    public const string Default = "default", Disabled = "disabled", Keys = "keys", Run = "run", Folder = "folder", Omni = "omni";
    public const string CycleCursor = "cursor.cycle", WebSearch = "edit.websearch", Magnifier = "desktop.magnifier";
    public const string MonitorsOff = "power.monitoroff", Screensaver = "power.screensaver", MediaPlayPause = "media.playpause", MediaNext = "media.next";
    private const string LayerPrefix = "layer.", TrackpadPrefix = "trackpad.";
    private const string NextMove = "next", PreviousMove = "previous", ResetMove = "reset", SwitchMove = "set.", HoldMove = "hold.";

    // Actions that change how the mouse behaves while a button is held or until it is pressed again (see MouseModes).
    public const string SlowHold = "cursor.slow", SlowToggle = "cursor.slowtoggle", MoveAsScroll = "scroll.movement";
    public const string AxisXHold = "axis.x.hold", AxisXToggle = "axis.x.toggle", AxisYHold = "axis.y.hold", AxisYToggle = "axis.y.toggle";
    public const string StickyPrefix = "sticky.", DoubleDrag = "click.doubledrag", WindowMove = "window.move", WindowSize = "window.size", Reveal = "desktop.reveal";

    // Where an action is listed in the Choose-the-action window: its section (pane 1) and the group heading inside it.
    private readonly record struct Home(string Section, string Heading);

    private static readonly Home Basic = new(ActionSections.Mouse, "Basic"), Clicks = new(ActionSections.Mouse, "Clicks and wheel"),
        Drag = new(ActionSections.Mouse, "Click and drag"), Cursor = new(ActionSections.Mouse, "Cursor and scrolling"),
        Edit = new(ActionSections.Keys, "Editing and keys"), Desktop = new(ActionSections.Keys, "Desktop"),
        VirtualDesktops = new(ActionSections.Keys, "Virtual desktops"), MagnifierGroup = new(ActionSections.Keys, "Magnifier"),
        WindowControl = new(ActionSections.Keys, "Windows"), Media = new(ActionSections.Keys, "Media"), Browser = new(ActionSections.Keys, "Browser"),
        Open = new(ActionSections.Launch, "Open"), Programs = new(ActionSections.Launch, "Programs and files"),
        Screen = new(ActionSections.Power, "Screen");

    private static readonly Home[] Repeatable = { Clicks, Cursor, Edit, Media, Browser };

    // A search word that finds every OmniControl action (they are listed under their own features' headings).
    private const string OmniControl = "OmniControl";

    /// What an action does: send a keystroke, run a program or folder, or neither (the runner or the modes handle it by id); an action that
    /// is not a keystroke may name its <see cref="HotkeyAction.KeyEquivalent"/>, sent instead while a remote-input window is in front.
    private sealed record Def(string Id, Home Where, string Name, string? KeyText = null, string? RunText = null, string Keywords = "", string? KeyEquivalent = null);

    private static readonly Def[] Fixed =
    {
        new(Default, Basic, "Default behaviour", Keywords: "no change don't intercept normal"),
        new(Disabled, Basic, "Do nothing", Keywords: "disable disabled off block"),

        new("click.left", Clicks, "Left click"), new("click.right", Clicks, "Right click"), new("click.middle", Clicks, "Middle click"),
        new("click.double", Clicks, "Double click"), new("click.back", Clicks, "Button 4 (back)", Keywords: "click button 4"),
        new("click.forward", Clicks, "Button 5 (forward)", Keywords: "click button 5"),
        new("scroll.up", Clicks, "Wheel up", Keywords: "mouse wheel"), new("scroll.down", Clicks, "Wheel down", Keywords: "mouse wheel"),
        new("scroll.left", Clicks, "Tilt left", Keywords: "mouse wheel tilt"), new("scroll.right", Clicks, "Tilt right", Keywords: "mouse wheel tilt"),

        new("sticky.left", Drag, "Sticky left button", Keywords: "click-drag hold lock"),
        new("sticky.left.x", Drag, "Sticky left button, X axis only", Keywords: "click-drag horizontal"),
        new("sticky.left.y", Drag, "Sticky left button, Y axis only", Keywords: "click-drag vertical"),
        new("sticky.right", Drag, "Sticky right button", Keywords: "click-drag hold lock"),
        new("sticky.middle", Drag, "Sticky middle button", Keywords: "click-drag hold lock"),
        new("sticky.back", Drag, "Sticky button 4", Keywords: "click-drag hold lock 4th"),
        new("sticky.forward", Drag, "Sticky button 5", Keywords: "click-drag hold lock 5th"),
        new(DoubleDrag, Drag, "Double click drag", Keywords: "double click and hold select word"),

        new(SlowHold, Cursor, "Slow cursor while held", Keywords: "slow down mouse cursor pressed"),
        new(SlowToggle, Cursor, "Slow cursor (toggle)", Keywords: "slow down mouse cursor sticky"),
        new(CycleCursor, Cursor, "Cycle cursor speed", Keywords: "mouse speed sensitivity dpi"),
        new(MoveAsScroll, Cursor, "Change movement to scroll while held", Keywords: "scroll by moving mouse"),
        new("scrollwin.up", Cursor, "Scroll window up", Keywords: "scroll without focus"), new("scrollwin.down", Cursor, "Scroll window down", Keywords: "scroll without focus"),
        new("scrollwin.left", Cursor, "Scroll window left", Keywords: "scroll without focus"), new("scrollwin.right", Cursor, "Scroll window right", Keywords: "scroll without focus"),
        new(AxisXHold, Cursor, "Lock X axis while held", Keywords: "lock mouse horizontal"), new(AxisXToggle, Cursor, "Lock X axis (toggle)", Keywords: "lock mouse horizontal"),
        new(AxisYHold, Cursor, "Lock Y axis while held", Keywords: "lock mouse vertical"), new(AxisYToggle, Cursor, "Lock Y axis (toggle)", Keywords: "lock mouse vertical"),

        new("edit.copy", Edit, "Copy", "Ctrl+C", Keywords: "ctrl c"), new("edit.paste", Edit, "Paste", "Ctrl+V", Keywords: "ctrl v"),
        new("edit.cut", Edit, "Cut", "Ctrl+X", Keywords: "ctrl x"), new("edit.undo", Edit, "Undo", "Ctrl+Z", Keywords: "ctrl z"),
        new("edit.redo", Edit, "Redo", "Ctrl+Y", Keywords: "ctrl y"), new("edit.selectall", Edit, "Select all", "Ctrl+A", Keywords: "ctrl a"),
        new("edit.new", Edit, "New document", "Ctrl+N", Keywords: "ctrl n"), new("edit.open", Edit, "Open file", "Ctrl+O", Keywords: "ctrl o"), new("edit.save", Edit, "Save", "Ctrl+S", Keywords: "ctrl s"),
        new(WebSearch, Edit, "Web search selected text", Keywords: "google search copy selection"),
        new("key.enter", Edit, "Enter", "Enter", Keywords: "return"), new("key.escape", Edit, "Escape", "Esc"), new("key.help", Edit, "Help", "F1", Keywords: "open help f1"),
        new("key.printscreen", Edit, "Print Screen", "PrintScreen", Keywords: "prtsc screenshot"),
        new("key.printwindow", Edit, "Print Screen, active window", "Alt+PrintScreen", Keywords: "screenshot window alt"),
        new("desktop.screenshot", Edit, "Screenshot", "Win+Shift+S", Keywords: "snip snipping tool win shift s"),
        new("key.emoji", Edit, "Emoji panel", "Win+Period", Keywords: "open emoji keyboard"),

        new("desktop.taskview", Desktop, "Task view", "Win+Tab", Keywords: "windows tab overview"),
        new("desktop.switch", Desktop, "Switch window", "Alt+Tab", Keywords: "alt-tab"),
        new("desktop.next", Desktop, "Next window", "Alt+Esc"), new("desktop.previous", Desktop, "Previous window", "Alt+Shift+Esc"),
        new("desktop.show", Desktop, "Show or hide desktop", "Win+D", Keywords: "minimise all"),
        new(Reveal, Desktop, "Reveal desktop while held", Keywords: "peek desktop"),
        new("desktop.menu", Desktop, "System menu", "Win+X", Keywords: "power user win x"),
        new("desktop.settings", Desktop, "Windows Settings", "Win+I", Keywords: "pc settings win i"), new("desktop.search", Desktop, "Windows search", "Win+S", Keywords: "cortana win s"),
        new("desktop.notifications", Desktop, "Notifications", "Win+N", Keywords: "action center win n"),
        new("desktop.new", VirtualDesktops, "New virtual desktop", "Win+Ctrl+D"), new("desktop.close", VirtualDesktops, "Close virtual desktop", "Win+Ctrl+F4"),
        new("desktop.left", VirtualDesktops, "Switch virtual desktop left", "Win+Ctrl+Left"), new("desktop.right", VirtualDesktops, "Switch virtual desktop right", "Win+Ctrl+Right"),
        new(Magnifier, MagnifierGroup, "Toggle magnifier", Keywords: "zoom accessibility on off"),
        new("desktop.zoomin", MagnifierGroup, "Magnifier zoom in", "Win+Plus"), new("desktop.zoomout", MagnifierGroup, "Magnifier zoom out", "Win+Minus"),

        new("window.minimize", WindowControl, "Minimise window", Keywords: "minimize"), new("window.maximize", WindowControl, "Maximise or restore window", Keywords: "maximize"),
        new("window.restore", WindowControl, "Restore window"), new("window.close", WindowControl, "Close window", Keywords: "quit", KeyEquivalent: "Alt+F4"),
        new("window.altf4", WindowControl, "Close with Alt+F4", "Alt+F4", Keywords: "quit exit"),
        new("window.topmost", WindowControl, "Keep window on top", Keywords: "always on top pin"),
        new(WindowMove, WindowControl, "Drag to move window", Keywords: "alt drag hold"), new(WindowSize, WindowControl, "Drag to size window", Keywords: "resize hold"),
        new("snap.left", WindowControl, "Snap window left", "Win+Left", Keywords: "tile half"), new("snap.right", WindowControl, "Snap window right", "Win+Right", Keywords: "tile half"),
        new("snap.up", WindowControl, "Snap window up", "Win+Up", Keywords: "tile maximise"), new("snap.down", WindowControl, "Snap window down", "Win+Down", Keywords: "tile minimise"),

        new(MediaPlayPause, Media, "Play or pause", "MediaPlayPause"), new(MediaNext, Media, "Next track", "MediaNext"),
        new("media.prev", Media, "Previous track", "MediaPrevious", Keywords: "last"), new("media.stop", Media, "Stop playing", "MediaStop"),

        new("browser.back", Browser, "Browser back", "BrowserBack"), new("browser.forward", Browser, "Browser forward", "BrowserForward"),
        new("browser.refresh", Browser, "Refresh page", "BrowserRefresh", Keywords: "reload"), new("browser.stop", Browser, "Stop loading", "BrowserStop", Keywords: "browser stop"),
        new("browser.favourites", Browser, "Open favourites", "BrowserFavorites", Keywords: "favorites bookmarks"), new("browser.search", Browser, "Browser search", "BrowserSearch"),

        new("launch.browser", Open, "Web browser", "BrowserHome", Keywords: "open internet home page"), new("launch.mail", Open, "Email", "LaunchMail", Keywords: "launch mail"),
        new("launch.explorer", Open, "File Explorer", "Win+E", Keywords: "open files explorer"), new("launch.documents", Open, "Documents folder", RunText: "shell:Personal", Keywords: "my documents explorer"),
        new("launch.thispc", Open, "This PC", RunText: "shell:MyComputerFolder", Keywords: "my computer explorer"),
        new("launch.network", Open, "Network", RunText: "shell:NetworkPlacesFolder", Keywords: "network favourites explorer"),
        new("launch.control", Open, "Control Panel", RunText: "control.exe"), new("launch.calculator", Open, "Calculator", RunText: "calc.exe"),
        new("launch.rundialog", Open, "Run dialog", "Win+R", Keywords: "open run command"),

        new(MonitorsOff, Screen, "Turn monitors off", Keywords: "monitor power saving display sleep"), new(Screensaver, Screen, "Start screensaver"),
    };

    private static readonly IReadOnlyList<PickChoice> Parameterised = new PickChoice[]
    {
        new(Keys + ":", ActionSections.Keys, "Send keys", "Ctrl+Shift+S, or Ctrl+K, Ctrl+C", "send a shortcut remap key combination keystroke sequence simulated",
            new[] { MouseOptions.TriggerWithHold, MouseOptions.Times, MouseOptions.RepeatDelay, MouseOptions.RepeatEvery }, "Keys to send", Heading: Edit.Heading,
            ParamCheck: MouseOptions.KeysProblem, NamedByParam: true),
        new(Run + ":", Programs.Section, "Open a program, file or link", @"C:\Path\App.exe", "run application launch file url website address",
            new[] { MouseOptions.Arguments, MouseOptions.StartIn, MouseOptions.WindowStyle, MouseOptions.Administrator }, "Program, file or link", ParamBrowse: PathKind.File, Heading: Programs.Heading),
        new(Folder + ":", Programs.Section, "Open a folder", @"C:\Users\Me\Documents", "explorer at specified folder", ParamLabel: "Folder", ParamBrowse: PathKind.Folder, Heading: Programs.Heading),
    };

    private static readonly string[] WindowActions = { "window.minimize", "window.maximize", "window.restore", "window.close", "window.topmost" };

    // Desktop actions that make sense to repeat while the button is held (switching, zooming), unlike lock or show desktop.
    private static readonly string[] RepeatableDesktop = { "desktop.switch", "desktop.next", "desktop.previous", "desktop.left", "desktop.right", "desktop.zoomin", "desktop.zoomout" };

    /// True for the action whose button press is held back while apps' autoscroll is blocked: "movement to scroll" that does not keep scrolling until pressed again.
    public static bool HoldsClickBack(MouseAction action) => action.Id == MoveAsScroll && !action.Options.Bool(MouseOptions.ScrollSticky);

    /// The settings an action offers: its own, or the common "times / repeat" ones for actions that happen once per press.
    private static IReadOnlyList<ActionOption>? OptionsOf(string id, Home where)
    {
        switch (id)
        {
            case SlowHold: return new[] { MouseOptions.SlowSpeed, MouseOptions.HeldPointer };
            case SlowToggle: return new[] { MouseOptions.SlowSpeed };
            case AxisXHold or AxisYHold or WindowMove or WindowSize or Reveal or DoubleDrag: return new[] { MouseOptions.HeldPointer };
            case CycleCursor: return new[] { MouseOptions.CycleSpeeds };
            case MoveAsScroll:
                return new[]
                {
                    MouseOptions.ScrollDistance, MouseOptions.ScrollAmount, MouseOptions.ScrollDirection,
                    MouseOptions.ReverseVertical, MouseOptions.ReverseSideways, MouseOptions.ScrollSticky, MouseOptions.ScrollPointer,
                };
            case WebSearch: return new[] { MouseOptions.Engine, MouseOptions.CustomAddress };
            case "scrollwin.up" or "scrollwin.down" or "scrollwin.left" or "scrollwin.right": return MouseOptions.Common.Append(MouseOptions.ByPages).ToList();
        }
        if (WindowActions.Contains(id)) return new[] { MouseOptions.Target };
        if (id.StartsWith(StickyPrefix, StringComparison.Ordinal)) return new[] { MouseOptions.ReleaseAfter };
        if (IsMode(id)) return null;

        var repeatable = Repeatable.Contains(where) || RepeatableDesktop.Contains(id);
        return repeatable ? MouseOptions.Common : null;
    }

    /// The action's own settings plus the "also send" extras, except where they make no sense (do nothing, default, and for a button the
    /// layer actions). A trackpad gesture also gets "also let Windows do its own gesture" on a layer action: it decides whether Windows'
    /// gesture is turned off.
    private static IReadOnlyList<ActionOption>? WithAlongside(string id, IReadOnlyList<ActionOption>? own, bool gesture)
    {
        if (id is Default or Disabled) return own;
        var original = MouseOptions.OriginalFor(gesture);
        if (IsLayerAction(id)) return gesture ? (own ?? Array.Empty<ActionOption>()).Append(original).ToList() : own;
        // "Send keys" already sends keys: more to send alongside them belong in its own text.
        var extras = id.StartsWith(Keys, StringComparison.Ordinal) ? new[] { original } : new[] { original, MouseOptions.AlsoSend };
        if (id == MoveAsScroll && !gesture) extras = new[] { original, MouseOptions.AllowAutoscroll, MouseOptions.AlsoSend };
        return own is null ? extras : own.Concat(extras).ToList();
    }

    /// The list for a button: built-in actions, the layer actions, then one entry per OmniControl action of the master list
    /// (<see cref="HotkeyRegistry"/>), under that action's heading ("Displays", "Volume"...), found by its keywords too.
    /// <param name="hasRelease">
    /// False for an input that only fires (wheel, tilt, a trackpad gesture): the actions that last while a button is held are then
    /// greyed with the reason (<see cref="IsWhileHeld"/>), since nothing would ever end them.
    /// </param>
    /// <param name="gesture">
    /// True for a trackpad gesture: its "also keep the original" setting says Windows' own gesture, not a click, and its layer actions move
    /// the trackpad's layers. A mouse input's layer actions move the mouse's layers, plus "Hold for trackpad layer N" (a gesture can't be
    /// held, so a held button is how a trackpad layer is held).
    /// </param>
    public static List<PickChoice> Choices(IEnumerable<HotkeyAction> omniActions, bool hasRelease = true, bool gesture = false)
    {
        string? HeldOnly(string id) => !hasRelease && IsWhileHeld(id) ? NeedsHeldButton : null;
        return Build(omniActions, gesture, HeldOnly);
    }

    /// The list for an input's second or later action (<see cref="MouseSettings.ExtraActions"/>): the same entries without Default and
    /// Disabled, the "while held" ones greyed with the reason (only the first action can last while a button is held), and each entry's
    /// settings trimmed to what a one-shot action keeps (<see cref="MouseOptions.OneShot"/>).
    public static List<PickChoice> ExtraChoices(IEnumerable<HotkeyAction> omniActions, bool gesture = false) =>
        Build(omniActions, gesture, id => IsWhileHeld(id) ? OnlyFirstLasts : null)
            .Where(c => c.Spec is not (Default or Disabled))
            .Select(c => c with { Options = MouseOptions.OneShot(c.Options) })
            .ToList();

    private static List<PickChoice> Build(IEnumerable<HotkeyAction> omniActions, bool gesture, Func<string, string?> unavailable)
    {
        var list = Fixed.Select(d => new PickChoice(d.Id, d.Where.Section, d.Name, Keywords: d.Keywords, Options: WithAlongside(d.Id, OptionsOf(d.Id, d.Where), gesture),
            Unavailable: unavailable(d.Id), Heading: d.Where.Heading, Place: ActionSections.Find(d.Where.Section))).ToList();
        var layerChoices = gesture
            ? LayerChoices(InputFamily.Trackpad, withHold: false)
            : LayerChoices(InputFamily.Mouse, withHold: true).Concat(LayerChoices(InputFamily.Trackpad, withHold: true).Where(c => LayerActionOf(c.Spec)?.Move == LayerMove.Hold));
        // The layer actions follow the rest of the Mouse and Trackpad section's built-in actions.
        list.InsertRange(list.FindIndex(c => c.Category != ActionSections.Mouse),
            layerChoices.Select(c => c with { Options = WithAlongside(c.Spec, c.Options, gesture), Unavailable = unavailable(c.Spec) }));
        var parameterised = Parameterised.Select(c => c with { Options = WithAlongside(c.Spec, c.Options, gesture), Place = ActionSections.Find(c.Category) }).ToList();
        // "Send keys" leads its group: any key or combination is the most general of the keys actions.
        var sendKeys = parameterised.Single(c => c.Spec == Keys + ":");
        list.Insert(list.FindIndex(c => c.Heading == Edit.Heading), sendKeys);
        list.AddRange(parameterised.Where(c => c != sendKeys));
        list.AddRange(omniActions.Where(a => !a.CoveredByMousePicker).Select(a => OmniChoice(a, b => WithAlongside(Omni, OmniOptions(b), gesture))));
        // The Power section lists OmniControl's own power actions first; the two built-in screen actions follow them.
        return list.OrderBy(c => c.Category == Screen.Section && c.Heading == Screen.Heading ? 1 : 0).ToList();
    }

    /// The entries of the Actions and hotkeys window: every master action (and the buttons of its lists) with the settings its hotkeys and
    /// rules use (<see cref="OwnOptions"/>: the action's defaults, not the "times / repeat" ones of an input), what a key keeps of its
    /// own (a custom action's editor, remove, rename) and why it does nothing now.
    public static List<PickChoice> ManageChoices(IEnumerable<HotkeyAction> omniActions) =>
        omniActions.Select(a => OmniChoice(a, b => OwnOptions(b).Concat(b.ManageSettings ?? Array.Empty<ActionOption>()).ToList() is { Count: > 0 } own ? own : null) with { Unavailable = a.UnavailableReason }).ToList();

    /// The master action's id in an entry made by <see cref="OmniEntry"/>; null for any other entry.
    public static string? OmniIdOf(string spec) => Split(spec) is (Omni, var id) ? id : null;

    /// The picker entry of one master action (or of a picker list's button, <see cref="HotkeyAction.Command"/>); <paramref name="optionsOf"/> gives the settings an action without an editor offers.
    private static PickChoice OmniChoice(HotkeyAction a, Func<HotkeyAction, IReadOnlyList<ActionOption>?> optionsOf) =>
        OmniEntry(a, a.Command is not null ? null : a.Editor is { } editor ? editor.Options : optionsOf(a)) with
        {
            Remove = a.Remove,
            Editor = a.Editor,
            Create = a.Editor?.Create is { } create ? values => OmniChoice(create(values), optionsOf) : null,
            Opened = a.PickerOpened,
            Command = a.Command is null ? null : async from => (await a.Command(from)).Select(made => OmniChoice(made, optionsOf)).ToList(),
        };

    /// The entry of a master action without anything it can do beyond being listed: its section, group heading, name and search words. The one
    /// place an action is placed in a picker, shared by every list that offers OmniControl actions.
    public static PickChoice OmniEntry(HotkeyAction a, IReadOnlyList<ActionOption>? options = null, string? unavailable = null) =>
        new($"{Omni}:{a.Id}", a.Section, a.Title, Keywords: $"{OmniControl} {a.Keywords}", Options: options, Unavailable: unavailable, Heading: a.PickerHeading,
            Place: a.SectionPlace, RemoveText: a.RemoveText, Renaming: a.Renaming, IsListed: a.IsListed, Icon: a.ShownIcon, Family: a.Family, FamilyRole: a.FamilyRole, Tag: a.Tag, Remote: a.RemotePc);

    /// The settings of an OmniControl action of its own (a program, how far a level moves, a power countdown): what its hotkeys and rules use as its defaults.
    public static IReadOnlyList<ActionOption> OwnOptions(HotkeyAction a)
    {
        var own = new List<ActionOption>();
        if (a.Takes.HasFlag(ActionSettings.Launch)) own.AddRange(MouseOptions.Launching);
        if (a.Takes.HasFlag(ActionSettings.Step)) own.Add(MouseOptions.Step);
        if (a.Takes.HasFlag(ActionSettings.Countdown)) own.Add(MouseOptions.Countdown);
        if (a.Takes.HasFlag(ActionSettings.Force)) own.Add(MouseOptions.ForceClose);
        if (a.Takes.HasFlag(ActionSettings.Clicking)) own.AddRange(MouseOptions.RepeatClicking);
        return own;
    }

    /// The settings of an OmniControl action for a button or gesture: its own, then the common "times / repeat" ones where repeating makes sense.
    private static IReadOnlyList<ActionOption> OmniOptions(HotkeyAction a)
    {
        var own = OwnOptions(a);
        var repeats = !a.Takes.HasFlag(ActionSettings.Launch) && !a.Takes.HasFlag(ActionSettings.Countdown) && !a.Takes.HasFlag(ActionSettings.Clicking); // starting a program or powering off once per press is all they do
        return repeats ? own.Concat(MouseOptions.Common).ToList() : own;
    }

    /// Splits "keys:Ctrl+C" into "keys" and "Ctrl+C"; an action without a parameter gives an empty one.
    public static (string Id, string Param) Split(string spec)
    {
        var colon = spec.IndexOf(':');
        return colon < 0 ? (spec, string.Empty) : (spec[..colon], spec[(colon + 1)..]);
    }

    /// The keystroke a built-in action sends, or null when it is not a plain keystroke.
    public static string? KeyTextOf(string id) => Fixed.FirstOrDefault(d => d.Id == id)?.KeyText;

    /// The keystroke an action sends: a built-in one's, or a "keys:" action's own; null when it sends none.
    public static string? KeystrokeOf(MouseAction action) => action.Id == Keys ? action.Param : KeyTextOf(action.Id);

    /// The shortcut a built-in action that is not a keystroke is sent as while a remote-input window is in front, or null.
    public static string? KeyEquivalentOf(string id) => Fixed.FirstOrDefault(d => d.Id == id)?.KeyEquivalent;

    /// The program or folder a built-in action opens, or null when it does not open one.
    public static string? RunTextOf(string id) => Fixed.FirstOrDefault(d => d.Id == id)?.RunText;

    /// True for the built-in actions that open a program or folder window, whether by a keystroke (Win+E) or by running it.
    public static bool OpensWindow(string id) => Fixed.FirstOrDefault(d => d.Id == id)?.Where.Section == ActionSections.Launch;

    /// The id of a layer action of <paramref name="family"/> ("layer.next", "trackpad.layer.set.3").
    public static string LayerActionId(InputFamily family, LayerMove move, int layer = 0)
    {
        var own = (family == InputFamily.Trackpad ? TrackpadPrefix : string.Empty) + LayerPrefix;
        return own + move switch
        {
            LayerMove.Next => NextMove,
            LayerMove.Previous => PreviousMove,
            LayerMove.Reset => ResetMove,
            LayerMove.Switch => SwitchMove + layer,
            _ => HoldMove + layer,
        };
    }

    /// The layer action an id names, or null for any other action.
    public static LayerAction? LayerActionOf(string id)
    {
        var family = id.StartsWith(TrackpadPrefix + LayerPrefix, StringComparison.Ordinal) ? InputFamily.Trackpad : InputFamily.Mouse;
        var rest = family == InputFamily.Trackpad ? id[TrackpadPrefix.Length..] : id;
        if (!rest.StartsWith(LayerPrefix, StringComparison.Ordinal)) return null;
        rest = rest[LayerPrefix.Length..];
        return rest switch
        {
            NextMove => new(family, LayerMove.Next),
            PreviousMove => new(family, LayerMove.Previous),
            ResetMove => new(family, LayerMove.Reset),
            _ when Number(rest, SwitchMove) is { } n => new(family, LayerMove.Switch, n),
            _ when Number(rest, HoldMove) is { } n => new(family, LayerMove.Hold, n),
            _ => null,
        };
    }

    /// A stored action, with a layer action of the other family turned into the same action for <paramref name="family"/> (options kept); any other action as it is.
    public static string MoveLayerActionTo(InputFamily family, string spec)
    {
        var (id, rest) = SplitId(spec);
        return LayerActionOf(id) is { } layer && layer.Family != family ? LayerActionId(family, layer.Move, layer.Layer) + rest : spec;
    }

    /// True for the actions that change a layer (handled by <see cref="MouseService"/> itself, not the runner).
    public static bool IsLayerAction(string id) => LayerActionOf(id) is not null;

    /// One family's layer actions for a picker: next, previous, back to 1, then "switch to" and (with <paramref name="withHold"/>) "hold for" per layer.
    private static IEnumerable<PickChoice> LayerChoices(InputFamily family, bool withHold)
    {
        var heading = family == InputFamily.Trackpad ? "Trackpad layers" : "Layers";
        var noun = family == InputFamily.Trackpad ? $"{family.Noun()} layer" : "layer";
        var wrap = new[] { MouseOptions.Wrap };
        yield return new PickChoice(LayerActionId(family, LayerMove.Next), ActionSections.Mouse, $"Next {noun}", Keywords: $"{family.Noun()} layer", Options: wrap, Heading: heading);
        yield return new PickChoice(LayerActionId(family, LayerMove.Previous), ActionSections.Mouse, $"Previous {noun}", Keywords: $"{family.Noun()} layer", Options: wrap, Heading: heading);
        yield return new PickChoice(LayerActionId(family, LayerMove.Reset), ActionSections.Mouse, $"Back to {noun} 1", Keywords: $"{family.Noun()} layer modifier revert", Heading: heading);
        for (var layer = 2; layer <= MouseSettings.Layers; layer++)
        {
            yield return new PickChoice(LayerActionId(family, LayerMove.Switch, layer), ActionSections.Mouse, $"Switch to {noun} {layer}", Keywords: $"{family.Noun()} layer {layer}", Heading: heading);
            if (withHold)
                yield return new PickChoice(LayerActionId(family, LayerMove.Hold, layer), ActionSections.Mouse, $"Hold for {noun} {layer}", Keywords: $"{family.Noun()} layer {layer} modifier", Heading: heading);
        }
    }

    // The id and whatever follows it (":parameter", "|options"), split without parsing the options.
    private static (string Id, string After) SplitId(string spec)
    {
        var end = spec.IndexOfAny(new[] { ':', '|' });
        return end < 0 ? (spec, string.Empty) : (spec[..end], spec[end..]);
    }

    private static int? Number(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(text[prefix.Length..], out var n) ? n : null;

    /// Why a lasting action can't be picked for an input that only fires (wheel, tilt, a trackpad gesture).
    public const string NeedsHeldButton = "Needs a button that is held down and let go";

    /// True for an action that can be an input's second or later action: one that runs once (not Default, Disabled or a "while held" one).
    public static bool CanRunLater(string id) => id is not (Default or Disabled) && !IsWhileHeld(id);

    /// Why a lasting action can't be an input's second or later action: those run once, one after another.
    public const string OnlyFirstLasts = "Only the first action can last while the button is held";

    /// True for the actions that last while the button is held and end on its release (they also offer a "pointer while held"
    /// setting): an input without a release must never start one.
    public static bool IsWhileHeld(string id) =>
        id is MoveAsScroll or SlowHold or AxisXHold or AxisYHold or WindowMove or WindowSize or Reveal or DoubleDrag || LayerActionOf(id)?.Move == LayerMove.Hold;

    /// True for actions that change how the mouse behaves while held or until pressed again (handled by <see cref="MouseModes"/>).
    public static bool IsMode(string id) =>
        id is SlowHold or SlowToggle or MoveAsScroll or AxisXHold or AxisXToggle or AxisYHold or AxisYToggle
            or DoubleDrag or WindowMove or WindowSize or Reveal
        || id.StartsWith(StickyPrefix, StringComparison.Ordinal);
}
