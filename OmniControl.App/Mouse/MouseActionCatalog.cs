using OmniControl.App.Hotkeys;

namespace OmniControl.App.Mouse;

/// <summary>How a layer action moves its family's layer.</summary>
public enum LayerMove { Next, Previous, Reset, Switch, Hold }

/// <summary>A layer action read from its id: whose layer it moves (mouse or trackpad), how, and to which layer (Switch and Hold).</summary>
public sealed record LayerAction(InputFamily Family, LayerMove Move, int Layer = 0);

/// <summary>
/// Every action a mouse button can be given, in one table. An action is stored as text: "id", or "id:parameter" for
/// "keys:Ctrl+Shift+T", "run:C:\app.exe", "folder:C:\Docs" and "omni:&lt;hotkey id&gt;" (any OmniControl hotkey action).
/// Layer actions are "layer.next", "layer.previous", "layer.reset", "layer.set.N" and "layer.hold.N" for the mouse's layers, and the same
/// with "trackpad." in front for the trackpad's.
/// </summary>
public static class MouseActionCatalog
{
    public const string Default = "default", Disabled = "disabled", Keys = "keys", Run = "run", Folder = "folder", Omni = "omni";
    public const string CycleCursor = "cursor.cycle", WebSearch = "edit.websearch", Magnifier = "desktop.magnifier";
    private const string LayerPrefix = "layer.", TrackpadPrefix = "trackpad.";
    private const string NextMove = "next", PreviousMove = "previous", ResetMove = "reset", SwitchMove = "set.", HoldMove = "hold.";

    // Actions that change how the mouse behaves while a button is held or until it is pressed again (see MouseModes).
    public const string SlowHold = "cursor.slow", SlowToggle = "cursor.slowtoggle", MoveAsScroll = "scroll.movement";
    public const string AxisXHold = "axis.x.hold", AxisXToggle = "axis.x.toggle", AxisYHold = "axis.y.hold", AxisYToggle = "axis.y.toggle";
    public const string StickyPrefix = "sticky.", DoubleDrag = "click.doubledrag", WindowMove = "window.move", WindowSize = "window.size", Reveal = "desktop.reveal";

    private const string Basic = "Basic", Clicks = "Clicks and wheel", Drag = "Click and drag", Cursor = "Cursor and scrolling", Edit = "Edit and keys",
        Desktop = "Windows and desktop", WindowControl = "Window control", Media = "Media and volume", Browser = "Browser",
        Launch = "Launch", Layers = "Layers", TrackpadLayers = "Trackpad layers", Power = "Power and display", Custom = "Custom";

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
            case WebSearch: return new[] { MouseOptions.Engine, MouseOptions.CustomAddress };
            case "scrollwin.up" or "scrollwin.down" or "scrollwin.left" or "scrollwin.right": return MouseOptions.Common.Append(MouseOptions.ByPages).ToList();
        }
        if (WindowActions.Contains(id)) return new[] { MouseOptions.Target };
        if (id.StartsWith(StickyPrefix, StringComparison.Ordinal)) return new[] { MouseOptions.ReleaseAfter };
        if (IsMode(id)) return null;

        var repeatable = category is Clicks or Cursor or Edit or Media or Browser || RepeatableDesktop.Contains(id);
        return repeatable ? MouseOptions.Common : null;
    }

    /// <summary>
    /// The action's own settings plus the "also send" extras, except where they make no sense (do nothing, default, and for a button the
    /// layer actions). A trackpad gesture also gets "also let Windows do its own gesture" on a layer action: it decides whether Windows'
    /// gesture is turned off.
    /// </summary>
    private static IReadOnlyList<ActionOption>? WithAlongside(string id, IReadOnlyList<ActionOption>? own, bool gesture)
    {
        if (id is Default or Disabled) return own;
        var original = MouseOptions.OriginalFor(gesture);
        if (IsLayerAction(id)) return gesture ? (own ?? Array.Empty<ActionOption>()).Append(original).ToList() : own;
        // "Send a shortcut" is already a shortcut: a second one to send alongside it would only be a longer shortcut.
        var extras = id.StartsWith(Keys, StringComparison.Ordinal) ? new[] { original } : new[] { original, MouseOptions.AlsoSend };
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

    /// <summary>
    /// The list for an input's second or later action (<see cref="MouseSettings.ExtraActions"/>): the same entries without Default and
    /// Disabled, the "while held" ones greyed with the reason (only the first action can last while a button is held), and each entry's
    /// settings trimmed to what a one-shot action keeps (<see cref="MouseOptions.OneShot"/>).
    /// </summary>
    public static List<PickChoice> ExtraChoices(IEnumerable<HotkeyAction> omniActions, bool gesture = false) =>
        Build(omniActions, gesture, id => IsWhileHeld(id) ? OnlyFirstLasts : null)
            .Where(c => c.Spec is not (Default or Disabled))
            .Select(c => c with { Options = MouseOptions.OneShot(c.Options) })
            .ToList();

    private static List<PickChoice> Build(IEnumerable<HotkeyAction> omniActions, bool gesture, Func<string, string?> unavailable)
    {
        var list = Fixed.Select(d => new PickChoice(d.Id, d.Category, d.Name, Keywords: d.Keywords, Options: WithAlongside(d.Id, OptionsOf(d.Id, d.Category), gesture),
            Unavailable: unavailable(d.Id))).ToList();
        var layerChoices = gesture
            ? LayerChoices(InputFamily.Trackpad, withHold: false)
            : LayerChoices(InputFamily.Mouse, withHold: true).Concat(LayerChoices(InputFamily.Trackpad, withHold: true).Where(c => LayerActionOf(c.Spec)?.Move == LayerMove.Hold));
        // The layer actions keep their place in the list, just before "Power and display".
        list.InsertRange(list.FindIndex(c => c.Category == Power),
            layerChoices.Select(c => c with { Options = WithAlongside(c.Spec, c.Options, gesture), Unavailable = unavailable(c.Spec) }));
        list.AddRange(Parameterised.Select(c => c with { Options = WithAlongside(c.Spec, c.Options, gesture) }));
        list.AddRange(omniActions.Select(a => new PickChoice($"{Omni}:{a.Id}", a.Heading, a.Title, Keywords: $"{OmniControl} {a.Keywords}",
            Options: WithAlongside(Omni, MouseOptions.Common, gesture))));
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

    /// <summary>The keystroke an action sends: a built-in one's, or a "keys:" action's own; null when it sends none.</summary>
    public static string? KeystrokeOf(MouseAction action) => action.Id == Keys ? action.Param : KeyTextOf(action.Id);

    /// <summary>The program or folder a built-in action opens, or null when it does not open one.</summary>
    public static string? RunTextOf(string id) => Fixed.FirstOrDefault(d => d.Id == id)?.RunText;

    /// <summary>True for the built-in actions that open a program or folder window, whether by a keystroke (Win+E) or by running it.</summary>
    public static bool OpensWindow(string id) => Fixed.FirstOrDefault(d => d.Id == id)?.Category == Launch;

    /// <summary>The id of a layer action of <paramref name="family"/> ("layer.next", "trackpad.layer.set.3").</summary>
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

    /// <summary>The layer action an id names, or null for any other action.</summary>
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

    /// <summary>A stored action, with a layer action of the other family turned into the same action for <paramref name="family"/> (options kept); any other action as it is.</summary>
    public static string MoveLayerActionTo(InputFamily family, string spec)
    {
        var (id, rest) = SplitId(spec);
        return LayerActionOf(id) is { } layer && layer.Family != family ? LayerActionId(family, layer.Move, layer.Layer) + rest : spec;
    }

    /// <summary>True for the actions that change a layer (handled by <see cref="MouseService"/> itself, not the runner).</summary>
    public static bool IsLayerAction(string id) => LayerActionOf(id) is not null;

    /// <summary>One family's layer actions for a picker: next, previous, back to 1, then "switch to" and (with <paramref name="withHold"/>) "hold for" per layer.</summary>
    private static IEnumerable<PickChoice> LayerChoices(InputFamily family, bool withHold)
    {
        var category = family == InputFamily.Trackpad ? TrackpadLayers : Layers;
        var noun = family == InputFamily.Trackpad ? $"{family.Noun()} layer" : "layer";
        var wrap = new[] { MouseOptions.Wrap };
        yield return new PickChoice(LayerActionId(family, LayerMove.Next), category, $"Next {noun}", Keywords: $"{family.Noun()} layer", Options: wrap);
        yield return new PickChoice(LayerActionId(family, LayerMove.Previous), category, $"Previous {noun}", Keywords: $"{family.Noun()} layer", Options: wrap);
        yield return new PickChoice(LayerActionId(family, LayerMove.Reset), category, $"Back to {noun} 1", Keywords: $"{family.Noun()} layer modifier revert");
        for (var layer = 2; layer <= MouseSettings.Layers; layer++)
        {
            yield return new PickChoice(LayerActionId(family, LayerMove.Switch, layer), category, $"Switch to {noun} {layer}", Keywords: $"{family.Noun()} layer {layer}");
            if (withHold)
                yield return new PickChoice(LayerActionId(family, LayerMove.Hold, layer), category, $"Hold for {noun} {layer}", Keywords: $"{family.Noun()} layer {layer} modifier");
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

    /// <summary>Why a lasting action can't be picked for an input that only fires (wheel, tilt, a trackpad gesture).</summary>
    public const string NeedsHeldButton = "Needs a button that is held down and let go";

    /// <summary>True for an action that can be an input's second or later action: one that runs once (not Default, Disabled or a "while held" one).</summary>
    public static bool CanRunLater(string id) => id is not (Default or Disabled) && !IsWhileHeld(id);

    /// <summary>Why a lasting action can't be an input's second or later action: those run once, one after another.</summary>
    public const string OnlyFirstLasts = "Only the first action can last while the button is held";

    /// <summary>
    /// True for the actions that last while the button is held and end on its release (they also offer a "pointer while held"
    /// setting): an input without a release must never start one.
    /// </summary>
    public static bool IsWhileHeld(string id) =>
        id is MoveAsScroll or SlowHold or AxisXHold or AxisYHold or WindowMove or WindowSize or Reveal or DoubleDrag || LayerActionOf(id)?.Move == LayerMove.Hold;

    /// <summary>True for actions that change how the mouse behaves while held or until pressed again (handled by <see cref="MouseModes"/>).</summary>
    public static bool IsMode(string id) =>
        id is SlowHold or SlowToggle or MoveAsScroll or AxisXHold or AxisXToggle or AxisYHold or AxisYToggle
            or DoubleDrag or WindowMove or WindowSize or Reveal
        || id.StartsWith(StickyPrefix, StringComparison.Ordinal);
}
