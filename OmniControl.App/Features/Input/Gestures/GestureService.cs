using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Gestures;

/// Turns mouse-wheel scrolling into actions: over a screen area (taskbar, edges, title bar, desktop) or while keys are held.
/// A gesture that is on swallows the scroll, so the window under the pointer does not also scroll.
/// Middle-click gestures are only looked up here (<see cref="MiddleClickAt"/>); <see cref="MouseService"/> runs them like a button mapping.
/// The hook thread reads an immutable snapshot, so editing settings on the UI thread never blocks or races it.
public sealed class GestureService
{
    // Middle-click gestures have no edge areas, so a strip of 0 keeps an edge from hiding the desktop or title bar beneath it.
    private const int NoEdgeStrip = 0;

    private sealed record Active(string Step, OptionValues Options, AppScopeSetting? Scope);
    private sealed record Click(MouseAction Action, AppScopeSetting? Scope);
    private sealed record Snapshot(bool Enabled, int EdgeStrip, Dictionary<Modifiers, Active> Keys, Dictionary<PointerAreaKind, Active> Areas,
        Dictionary<PointerAreaKind, Click> MiddleAreas)
    {
        /// A wheel or middle-click gesture is on: Windows' real mouse input is needed.
        public bool NeedsHook { get; } = Enabled && Keys.Count + Areas.Count + MiddleAreas.Count > 0;
    }

    private readonly GestureActionRunner _runner;
    private readonly AppScopeGate _scopes;
    private volatile Snapshot _snapshot;

    public GestureSettings Settings { get; } = GestureSettings.Load();

    /// <param name="hook">The shared mouse hook (the caller starts and stops it).</param>
    /// <param name="runLevel">Moves an OmniControl level (by its id) up or down by a percent; false when no action moves it.</param>
    /// <param name="scopes">Says whether a gesture works in the app the user is in (the Apps chip of its group and cards).</param>
    public GestureService(MouseHook hook, Func<string, bool, int, bool> runLevel, Action<string> report, AppScopeGate scopes)
    {
        _scopes = scopes;
        _runner = new GestureActionRunner(runLevel, report);
        _snapshot = TakeSnapshot();
        hook.AddHandler(Handle, () => _snapshot.NeedsHook);
    }

    /// Raised after every settings change (the custom rules follow the on/off switch and the edge strip; the hook follows what is on).
    public event Action? Applied;

    /// Turns every gesture on or off.
    public void SetEnabled(bool on)
    {
        Settings.Enabled = on;
        Apply();
    }

    /// Sets the width of the screen-edge strip.
    public void SetEdgeStrip(int value)
    {
        Settings.EdgeStrip = value;
        Apply();
    }

    /// Sets the apps every gesture of one group works in.
    public void SetGroupScope(string group, AppScopeSetting? scope)
    {
        Settings.SetGroupScope(group, scope);
        Apply();
    }

    /// Sets the apps one gesture works in (null = the group's).
    public void SetScope(GestureTrigger trigger, AppScopeSetting? scope)
    {
        Settings.RuleFor(trigger).Scope = scope;
        Apply();
    }

    /// Replaces the whole gesture setup (Restore).
    public void Replace(GestureSettings other)
    {
        Settings.CopyFrom(other);
        Apply();
    }

    /// Takes gestures.json from disk as it is now (an import wrote it) and applies it like any change.
    public bool ReloadFromDisk()
    {
        Replace(GestureSettings.Load());
        return true;
    }

    /// Call after any settings change: saves, and hands the hook a fresh copy.
    public void Apply()
    {
        Settings.Save();
        _snapshot = TakeSnapshot();
        Applied?.Invoke();
    }

    /// A gesture whose action's id moved (a merged smart device's actions, <c>HotkeyRegistry.Moved</c>) now names the new id.
    public void FollowMoved(IReadOnlyDictionary<string, string> moved)
    {
        var changed = false;
        foreach (var rule in Settings.Rules.Values)
        {
            var now = MovedActions.RewriteAction(rule.Action, moved);
            changed |= now != rule.Action;
            rule.Action = now;
        }
        if (changed) Apply();
    }

    private Snapshot TakeSnapshot()
    {
        // An unassigned wheel gesture (empty action) is left out, so its area does not take the scroll.
        var on = GestureCatalog.Triggers.Where(t => Settings.RuleFor(t) is { On: true } rule && (t.MiddleClick || rule.Action.Length > 0)).ToList();
        Active ActiveFor(GestureTrigger t)
        {
            var rule = Settings.RuleFor(t);
            return new Active(rule.Action, new OptionValues(new Dictionary<string, string>(rule.Options)), Settings.ScopeOf(t));
        }
        Click ClickFor(GestureTrigger t)
        {
            var rule = Settings.RuleFor(t);
            return new Click(MouseAction.From(rule.Action, rule.Options), Settings.ScopeOf(t));
        }

        var wheel = on.Where(t => !t.MiddleClick).ToList();
        return new Snapshot(
            Settings.Enabled,
            Settings.ClampedEdgeStrip(),
            wheel.Where(t => t.Keys != Modifiers.None).ToDictionary(t => t.Keys, ActiveFor),
            wheel.Where(t => t.Area != PointerAreaKind.Elsewhere).ToDictionary(t => t.Area, ActiveFor),
            on.Where(t => t.MiddleClick).ToDictionary(t => t.Area, ClickFor));
    }

    /// Runs on the hook thread for every button press: the action of the middle-click gesture that is on for the area under the pointer,
    /// or null (not a middle press, gestures off, no such gesture there, a modifier key held, or outside its apps: the button's mapping then
    /// decides). Costs nothing while no middle-click gesture is on.
    public MouseAction? MiddleClickAt(MouseEvent ev)
    {
        var snapshot = _snapshot;
        if (ev.Button != MouseButton.Middle || !ev.IsDown || !snapshot.Enabled || snapshot.MiddleAreas.Count == 0) return null;
        // As for the wheel: areas only count with no modifier held, so Ctrl+middle click keeps its own meaning.
        if (KeyboardState.Held() != Modifiers.None) return null;
        var area = PointerArea.At(ev.X, ev.Y, NoEdgeStrip, snapshot.MiddleAreas.ContainsKey(PointerAreaKind.TitleBar));
        return snapshot.MiddleAreas.TryGetValue(area, out var click) && _scopes.Allows(click.Scope) ? click.Action : null;
    }

    /// Runs on the hook thread for every input; only the wheel matters here. Returns true to swallow it.
    private bool Handle(MouseEvent ev)
    {
        var snapshot = _snapshot;
        if (!snapshot.Enabled || ev.Button is not (MouseButton.WheelUp or MouseButton.WheelDown)) return false;
        if (snapshot.Keys.Count == 0 && snapshot.Areas.Count == 0) return false;

        // Keys held decide first; areas only count when no modifier is held, so Ctrl+wheel zoom over the taskbar stays Ctrl+wheel.
        var held = KeyboardState.Held();
        Active? active = null;
        if (held != Modifiers.None) snapshot.Keys.TryGetValue(held, out active);
        else if (snapshot.Areas.Count > 0)
            snapshot.Areas.TryGetValue(PointerArea.At(ev.X, ev.Y, snapshot.EdgeStrip, snapshot.Areas.ContainsKey(PointerAreaKind.TitleBar)), out active);
        if (active is null || !_scopes.Allows(active.Scope)) return false; // outside its apps the scroll goes to the window as usual

        var wheelUp = ev.Button == MouseButton.WheelUp;
        Task.Run(() => _runner.Run(active.Step, active.Options, wheelUp, ev));
        return true;
    }
}
