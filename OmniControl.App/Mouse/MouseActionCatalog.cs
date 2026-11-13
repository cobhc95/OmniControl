using OmniControl.App.Hotkeys;

namespace OmniControl.App.Mouse;

/// <summary>
/// Every action a mouse button can be given, in one table. An action is stored as text: "id", or "id:parameter" for
/// "keys:Ctrl+Shift+T", "run:C:\app.exe", "folder:C:\Docs" and "omni:&lt;hotkey id&gt;" (any OmniControl hotkey action).
/// </summary>
public static class MouseActionCatalog
{
    public const string Default = "default", Disabled = "disabled", Keys = "keys", Run = "run", Folder = "folder", Omni = "omni";
    public const string NextLayer = "layer.next", PreviousLayer = "layer.previous", ResetLayer = "layer.reset";
    public const string CycleCursor = "cursor.cycle", WebSearch = "edit.websearch", Magnifier = "desktop.magnifier";
    private const string HoldLayer = "layer.hold.", SetLayer = "layer.set.";

    // Actions that change how the mouse behaves while a button is held or until it is pressed again (see MouseModes).
    public const string SlowHold = "cursor.slow", SlowToggle = "cursor.slowtoggle", MoveAsScroll = "scroll.movement";
    public const string AxisXHold = "axis.x.hold", AxisXToggle = "axis.x.toggle", AxisYHold = "axis.y.hold", AxisYToggle = "axis.y.toggle";
    public const string StickyPrefix = "sticky.", DoubleDrag = "click.doubledrag", WindowMove = "window.move", WindowSize = "window.size", Reveal = "desktop.reveal";

    private const string Basic = "Basic", Clicks = "Clicks and wheel", Drag = "Click and drag", Cursor = "Cursor and scrolling", Edit = "Edit and keys",
        Desktop = "Windows and desktop", WindowControl = "Window control", Media = "Media and volume", Browser = "Browser",
        Launch = "Launch", Layers = "Layers", Power = "Power and display", Custom = "Custom";

    // A search word that finds every OmniControl action (they are listed under their own features' headings).
    private const string OmniControl = "OmniControl";

    /// <summary>What an action does: send a keystroke, run a program or folder, or neither (the runner or the modes handle it by id).</summary>
    private sealed record Def(string Id, string Category, string Name, string? KeyText = null, string? RunText = null, string Keywords = "");

    private static readonly Def[] Fixed =
    {
        new(Default, Basic, "Default behaviour", Keywords: "no change don't intercept normal"),
        new(Disabled, Basic, "Disabled (do nothing)", Keywords: "disable off block"),

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
        new("edit.new", Edit, "New document (Ctrl+N)", "Ctrl+N"), new("edit.open", Edit, "Open file (Ctrl+O)", "Ctrl+O"), new("edit.save", Edit, "Save (Ctrl+S)", "Ctrl+S"),
        new(WebSearch, Edit, "Web search selected text", Keywords: "google search copy selection"),
        new("key.enter", Edit, "Enter", "Enter", Keywords: "return"), new("key.escape", Edit, "Escape", "Esc"), new("key.help", Edit, "Help (F1)", "F1", Keywords: "open help"),
        new("key.printscreen", Edit, "Print Screen", "PrintScreen", Keywords: "prtsc screenshot"),
        new("key.printwindow", Edit, "Print active window (Alt+Print Screen)", "Alt+PrintScreen", Keywords: "screenshot window"),
        new("desktop.screenshot", Edit, "Screenshot (Win+Shift+S)", "Win+Shift+S", Keywords: "snip snipping tool"),
        new("key.emoji", Edit, "Emoji panel", "Win+Period", Keywords: "open emoji keyboard"),

        new("desktop.taskview", Desktop, "Task view", "Win+Tab", Keywords: "windows tab overview"),
        new("desktop.switch", Desktop, "Switch window (Alt+Tab)", "Alt+Tab", Keywords: "alt-tab"),
        new("desktop.next", Desktop, "Next window", "Alt+Esc"), new("desktop.previous", Desktop, "Previous window", "Alt+Shift+Esc"),
        new("desktop.show", Desktop, "Show / hide desktop", "Win+D", Keywords: "minimise all"),
        new(Reveal, Desktop, "Reveal desktop while held", Keywords: "peek desktop"),
        new("desktop.new", Desktop, "Virtual desktop: new", "Win+Ctrl+D"), new("desktop.close", Desktop, "Virtual desktop: close", "Win+Ctrl+F4"),
        new("desktop.left", Desktop, "Virtual desktop: switch left", "Win+Ctrl+Left"), new("desktop.right", Desktop, "Virtual desktop: switch right", "Win+Ctrl+Right"),
        new("desktop.lock", Desktop, "Lock workstation", "Win+L", Keywords: "lock screen pc"), new("desktop.menu", Desktop, "System menu (Win+X)", "Win+X", Keywords: "power user"),
        new("desktop.settings", Desktop, "Settings (Win+I)", "Win+I", Keywords: "pc settings"), new("desktop.search", Desktop, "Search (Win+S)", "Win+S", Keywords: "cortana"),
        new("desktop.notifications", Desktop, "Notifications (Win+N)", "Win+N", Keywords: "action center"),
        new(Magnifier, Desktop, "Magnifier: toggle on / off", Keywords: "zoom accessibility"),
        new("desktop.zoomin", Desktop, "Magnifier: zoom in", "Win+Plus"), new("desktop.zoomout", Desktop, "Magnifier: zoom out", "Win+Minus"),

        new("window.minimize", WindowControl, "Minimise window", Keywords: "minimize"), new("window.maximize", WindowControl, "Maximise / restore window", Keywords: "maximize"),
        new("window.restore", WindowControl, "Restore window"), new("window.close", WindowControl, "Close window", Keywords: "quit"),
        new("window.altf4", WindowControl, "Close (Alt+F4)", "Alt+F4", Keywords: "quit exit"),
        new("window.topmost", WindowControl, "Keep window on top", Keywords: "always on top pin"),
        new(WindowMove, WindowControl, "Drag to move window", Keywords: "alt drag hold"), new(WindowSize, WindowControl, "Drag to size window", Keywords: "resize hold"),
        new("snap.left", WindowControl, "Snap window left", "Win+Left", Keywords: "tile half"), new("snap.right", WindowControl, "Snap window right", "Win+Right", Keywords: "tile half"),
        new("snap.up", WindowControl, "Snap window up", "Win+Up", Keywords: "tile maximise"), new("snap.down", WindowControl, "Snap window down", "Win+Down", Keywords: "tile minimise"),

        new("media.playpause", Media, "Play / pause", "MediaPlayPause"), new("media.next", Media, "Next track", "MediaNext"),
        new("media.prev", Media, "Last track", "MediaPrevious", Keywords: "previous"), new("media.stop", Media, "Stop", "MediaStop"),
        new("volume.up", Media, "Volume up", "VolumeUp", Keywords: "louder"), new("volume.down", Media, "Volume down", "VolumeDown", Keywords: "quieter"),
        new("volume.mute", Media, "Mute", "VolumeMute", Keywords: "silence"),

        new("browser.back", Browser, "Back", "BrowserBack"), new("browser.forward", Browser, "Forward", "BrowserForward"),
        new("browser.refresh", Browser, "Refresh", "BrowserRefresh", Keywords: "reload"), new("browser.stop", Browser, "Stop loading", "BrowserStop", Keywords: "browser stop"),
        new("browser.favourites", Browser, "Favourites", "BrowserFavorites", Keywords: "favorites bookmarks"), new("browser.search", Browser, "Search", "BrowserSearch"),
        new("browser.home", Browser, "Home page", "BrowserHome"),

        new("launch.browser", Launch, "Web browser", "BrowserHome", Keywords: "open internet"), new("launch.mail", Launch, "Email", "LaunchMail", Keywords: "launch mail"),
        new("launch.explorer", Launch, "Explorer", "Win+E", Keywords: "open files"), new("launch.documents", Launch, "Documents folder", RunText: "shell:Personal", Keywords: "my documents explorer"),
        new("launch.thispc", Launch, "This PC", RunText: "shell:MyComputerFolder", Keywords: "my computer explorer"),
        new("launch.network", Launch, "Network", RunText: "shell:NetworkPlacesFolder", Keywords: "network favourites explorer"),
        new("launch.control", Launch, "Control Panel", RunText: "control.exe"), new("launch.calculator", Launch, "Calculator", RunText: "calc.exe"),
        new("launch.rundialog", Launch, "Run dialog", "Win+R", Keywords: "open run command"),

        new(NextLayer, Layers, "Next layer"), new(PreviousLayer, Layers, "Previous layer"), new(ResetLayer, Layers, "Back to layer 1", Keywords: "layer modifier revert"),

        new("power.monitoroff", Power, "Turn monitors off", Keywords: "monitor power saving display sleep"), new("power.screensaver", Power, "Start screensaver"),
    };

    private static readonly IReadOnlyList<PickChoice> Parameterised = new PickChoice[]
    {
        new(Keys + ":", Custom, "Send a shortcut", "Ctrl+Shift+T", "simulated keys keystroke",
            new[] { MouseOptions.TriggerWithHold, MouseOptions.Times, MouseOptions.RepeatDelay, MouseOptions.RepeatEvery }, "Shortcut to send"),
        new(Run + ":", Custom, "Open a program, file or link", @"C:\Path\App.exe", "run application launch file url website address",
            new[] { MouseOptions.Arguments, MouseOptions.StartIn, MouseOptions.WindowStyle, MouseOptions.Administrator }, "Program, file or link"),
        new(Folder + ":", Launch, "Open a folder", @"C:\Users\Me\Documents", "explorer at specified folder", ParamLabel: "Folder"),
    };

    private static readonly string[] WindowActions = { "window.minimize", "window.maximize", "window.restore", "window.close", "window.topmost" };

    // Desktop actions that make sense to repeat while the button is held (switching, zooming), unlike lock or show desktop.
    private static readonly string[] RepeatableDesktop = { "desktop.switch", "desktop.next", "desktop.previous", "desktop.left", "desktop.right", "desktop.zoomin", "desktop.zoomout" };

    /// <summary>The settings an action offers: its own, or the common "times / repeat" ones for actions that happen once per press.</summary>
    private static IReadOnlyList<ActionOption>? OptionsOf(string id, string category)
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
            case NextLayer or PreviousLayer: return new[] { MouseOptions.Wrap };
            case ResetLayer: return null;
            case WebSearch: return new[] { MouseOptions.Engine, MouseOptions.CustomAddress };
            case "scrollwin.up" or "scrollwin.down" or "scrollwin.left" or "scrollwin.right": return MouseOptions.Common.Append(MouseOptions.ByPages).ToList();
        }
        if (WindowActions.Contains(id)) return new[] { MouseOptions.Target };
        if (id.StartsWith(StickyPrefix, StringComparison.Ordinal)) return new[] { MouseOptions.ReleaseAfter };
        if (IsMode(id)) return null;

        var repeatable = category is Clicks or Cursor or Edit or Media or Browser || RepeatableDesktop.Contains(id);
        return repeatable ? MouseOptions.Common : null;
    }

    /// <summary>The action's own settings plus the "also send" extras, except where they make no sense (do nothing, default, layers).</summary>
    private static IReadOnlyList<ActionOption>? WithAlongside(string id, IReadOnlyList<ActionOption>? own)
    {
        if (id is Default or Disabled || IsLayerAction(id)) return own;
        // "Send a shortcut" is already a shortcut: a second one to send alongside it would only be a longer shortcut.
        var extras = id.StartsWith(Keys, StringComparison.Ordinal) ? new[] { MouseOptions.KeepOriginal } : MouseOptions.Alongside;
        return own is null ? extras : own.Concat(extras).ToList();
    }

    /// <summary>
    /// The list for a button: built-in actions, the layer actions, then one entry per OmniControl action of the master list
    /// (<see cref="HotkeyRegistry"/>), under that action's heading ("Displays", "Volume"...), found by its keywords too.
    /// </summary>
    /// <param name="hasRelease">
    /// False for an input that only fires (wheel, tilt, a trackpad gesture): the actions that last while a button is held are then
    /// greyed with the reason (<see cref="IsWhileHeld"/>), since nothing would ever end them.
    /// </param>
    public static List<PickChoice> Choices(IEnumerable<HotkeyAction> omniActions, bool hasRelease = true)
    {
        string? HeldOnly(string id) => !hasRelease && IsWhileHeld(id) ? NeedsHeldButton : null;
        var list = Fixed.Select(d => new PickChoice(d.Id, d.Category, d.Name, Keywords: d.Keywords, Options: WithAlongside(d.Id, OptionsOf(d.Id, d.Category)),
            Unavailable: HeldOnly(d.Id))).ToList();
        for (var layer = 2; layer <= MouseSettings.Layers; layer++)
        {
            list.Add(new PickChoice(SetLayer + layer, Layers, $"Switch to layer {layer}", Keywords: $"layer {layer}"));
            list.Add(new PickChoice(HoldLayer + layer, Layers, $"Hold for layer {layer}", Keywords: $"layer {layer} modifier", Unavailable: HeldOnly(HoldLayer + layer)));
        }
        list.AddRange(Parameterised.Select(c => c with { Options = WithAlongside(c.Spec, c.Options) }));
        list.AddRange(omniActions.Select(a => new PickChoice($"{Omni}:{a.Id}", a.Heading, a.Title, Keywords: $"{OmniControl} {a.Keywords}",
            Options: WithAlongside(Omni, MouseOptions.Common))));
        return list;
    }

    /// <summary>Splits "keys:Ctrl+C" into "keys" and "Ctrl+C"; an action without a parameter gives an empty one.</summary>
    public static (string Id, string Param) Split(string spec)
    {
        var colon = spec.IndexOf(':');
        return colon < 0 ? (spec, string.Empty) : (spec[..colon], spec[(colon + 1)..]);
    }

    /// <summary>The keystroke a built-in action sends, or null when it is not a plain keystroke.</summary>
    public static string? KeyTextOf(string id) => Fixed.FirstOrDefault(d => d.Id == id)?.KeyText;

    /// <summary>The program or folder a built-in action opens, or null when it does not open one.</summary>
    public static string? RunTextOf(string id) => Fixed.FirstOrDefault(d => d.Id == id)?.RunText;

    /// <summary>True for the built-in actions that open a program or folder window, whether by a keystroke (Win+E) or by running it.</summary>
    public static bool OpensWindow(string id) => Fixed.FirstOrDefault(d => d.Id == id)?.Category == Launch;

    /// <summary>The layer a "hold for layer N" action switches to while held, or null for any other action.</summary>
    public static int? HeldLayer(string id) => LayerOf(id, HoldLayer);

    /// <summary>The layer a "switch to layer N" action jumps to, or null for any other action.</summary>
    public static int? SwitchedLayer(string id) => LayerOf(id, SetLayer);

    /// <summary>True for the actions that change the mouse layer (handled by <see cref="MouseService"/> itself, not the runner).</summary>
    public static bool IsLayerAction(string id) =>
        HeldLayer(id) is not null || SwitchedLayer(id) is not null || id is NextLayer or PreviousLayer or ResetLayer;

    private static int? LayerOf(string id, string prefix) =>
        id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id[prefix.Length..], out var n) ? n : null;

    /// <summary>Why a lasting action can't be picked for an input that only fires (wheel, tilt, a trackpad gesture).</summary>
    public const string NeedsHeldButton = "Needs a button that is held down and let go";

    /// <summary>
    /// True for the actions that last while the button is held and end on its release (they also offer a "pointer while held"
    /// setting): an input without a release must never start one.
    /// </summary>
    public static bool IsWhileHeld(string id) =>
        id is MoveAsScroll or SlowHold or AxisXHold or AxisYHold or WindowMove or WindowSize or Reveal or DoubleDrag || HeldLayer(id) is not null;

    /// <summary>True for actions that change how the mouse behaves while held or until pressed again (handled by <see cref="MouseModes"/>).</summary>
    public static bool IsMode(string id) =>
        id is SlowHold or SlowToggle or MoveAsScroll or AxisXHold or AxisXToggle or AxisYHold or AxisYToggle
            or DoubleDrag or WindowMove or WindowSize or Reveal
        || id.StartsWith(StickyPrefix, StringComparison.Ordinal);
}
