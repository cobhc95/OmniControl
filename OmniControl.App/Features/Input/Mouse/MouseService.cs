using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Shared.Scripts;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// Remaps the mouse: owns the hook, picks the layer for each input and runs the mapped action with its options.
/// The hook thread only reads an immutable snapshot, so editing settings on the UI thread never blocks or races it.
public sealed partial class MouseService : ILayerControl, ISharedOptions, IDisposable
{
    /// The settings as the hook reads them: the first action per key, the others in order, and the apps scope of each limited button.
    private sealed record Snapshot(Dictionary<string, MouseAction> Actions, Dictionary<string, MouseAction[]> Extras, Dictionary<MouseButton, AppScopeSetting> Scopes, bool BlockAutoscroll,
        bool PauseInFullScreen, AppScopeSetting? NeverActIn, bool SendToRemoteAsKeys)
    {
        public MouseAction[] ExtrasOf(string key) => Extras.TryGetValue(key, out var extras) ? extras : NoExtras;

        /// A mouse input has an action on some layer, or the autoscroll block is on: Windows' real mouse input is needed.
        public bool NeedsHook { get; } = BlockAutoscroll || Maps(Actions, Extras, InputFamily.Mouse);

        /// The autoscroll block can swallow a middle press: it is on and the middle button is not mapped on every layer.
        public bool WatchesAutoscroll { get; } = BlockAutoscroll && !Enumerable.Range(1, MouseSettings.Layers)
            .All(layer => Actions.TryGetValue(MouseSettings.KeyOf(layer, MouseButton.Middle), out var action) && action.Id != MouseActionCatalog.Default);

        /// A trackpad gesture has an action on some layer: the touchpad watcher is needed.
        public bool NeedsTrackpad { get; } = Maps(Actions, Extras, InputFamily.Trackpad);

        private static bool Maps(Dictionary<string, MouseAction> actions, Dictionary<string, MouseAction[]> extras, InputFamily family) =>
            actions.Any(a => a.Value.Id != MouseActionCatalog.Default && FamilyOfKey(a.Key) == family)
            || extras.Any(e => e.Value.Length > 0 && FamilyOfKey(e.Key) == family);

        private static InputFamily? FamilyOfKey(string key) => MouseSettings.ParseKey(key)?.Button.FamilyOf();
    }

    private static readonly MouseAction[] NoExtras = Array.Empty<MouseAction>();

    private readonly MouseActionRunner _runner;
    private readonly MouseModes _modes;
    private readonly Func<string, RunSettings, Action?> _holdOmniAction;
    private readonly AutoscrollWatcher _autoscroll = new(); // keeps the middle-press answer ready so the hook never waits for it
    private readonly FullScreenWatcher _fullScreen = new(); // keeps "a full-screen app is in front" ready for the same reason
    private readonly Action<string> _report;
    private readonly Action<string> _log;
    private readonly Func<MouseEvent, MouseAction?> _gestureAt;
    private readonly AppScopeGate _scopes;

    private readonly object _gate = new();
    private readonly HashSet<MouseButton> _swallowed = new();           // pressed buttons whose release must be swallowed too
    private readonly Dictionary<MouseButton, Action> _undoOnRelease = new(); // what to do when a button comes up: undo a "while held" action, or run a "when released" one
    private readonly MouseHook _hook;
    private readonly LayerState _layers = new();                        // the mouse's and the trackpad's layer in force
    private readonly HashSet<MouseButton> _sequenceRunning = new();     // inputs whose second and later actions are still running
    private volatile Snapshot _snapshot;

    public MouseSettings Settings { get; } = MouseSettings.Load();

    /// The "Repeated clicking" action's engine; its master action (registered by the Mouse page) starts and stops it.
    public RepeatedClicking Clicking { get; } = new();

    /// Raised on the UI thread with a family and its layer now in force, whenever a button, gesture or hotkey changes it (also while a
    /// "hold for layer" button is down).
    public event Action<InputFamily, int>? LayerChanged;

    /// Raised on the UI thread when a layer is renamed, cleared, or the list of layers shown changes.
    public event Action? LayersChanged;

    /// The layer of <paramref name="family"/> in force now (a held "hold for layer" button counts).
    public int LayerOf(InputFamily family) => _layers.Current(family);

    /// Raised on the hook thread for every real button, wheel or tilt press, mapped or not. Handlers must return at once.
    public event Action<MouseButton>? ButtonPressed;


    /// <param name="hook">The shared mouse hook (the caller starts and stops it).</param>
    /// <param name="gestureAt">
    /// Asked first on the hook thread for every press: the action a gesture puts on this press instead of the button's mapping (a middle
    /// click over a screen area), or null.
    /// </param>
    /// <param name="scopes">Says whether a button's or trackpad gesture's mapping works in the app the user is in (the Apps chip of the Mouse
    /// Buttons and Trackpad groups and their cards).</param>
    /// <param name="holdOmniAction">Starts an OmniControl action that lasts while the button is held and returns what ends it (null: it does not last).</param>
    /// <param name="states">The master list of states: the mouse's and the trackpad's layer in force are registered there.</param>
    /// <param name="keyEquivalentOf">The master action's <see cref="HotkeyAction.KeyEquivalent"/> by id, or null.</param>
    /// <param name="ask">Asks the person at this PC (UI thread): whether a button or gesture may start what it names.</param>
    public MouseService(MouseHook hook, Func<string, RunSettings?, bool> runOmniAction, Func<string, RunSettings, Action?> holdOmniAction, Action<string> report, Func<MouseEvent, MouseAction?> gestureAt, AppScopeGate scopes,
        States.IStateRegistry states, Func<string, string?> keyEquivalentOf, Func<DialogMessage, Task<bool>> ask)
    {
        _scopes = scopes;
        _holdOmniAction = holdOmniAction;
        _report = report;
        _log = hook.Diagnose;
        _gestureAt = gestureAt;
        Approvals = new CommandApprovals(() => Settings.Approved!, Settings.Save, ask);
        AllowMappingsOnce();
        _snapshot = TakeSnapshot();
        _runner = new MouseActionRunner(runOmniAction, Say, hook.Diagnose, () => _snapshot.SendToRemoteAsKeys, keyEquivalentOf, MayStart);
        _modes = new MouseModes(hook);
        ApplyWatchers();
        hook.PassAll = PassesEverything;
        hook.AddHandler(Handle, NeedsHook);
        _hook = hook;
        _pressWatch.Changed += OnPressWatchChanged;
        hook.Reinstalled += ReleaseAllHeld;
        hook.PointerMoved += _autoscroll.Moved;
        _states = states;
        RegisterStates();
        if (_taskFollows = Settings.RunAsAdministrator) AdministratorMode.FollowSetting(true, report); // an elevated start removes a task that isn't OmniControl's own
    }

    /// Raised on the UI thread after <see cref="Apply"/>: a mapping or a switch may have changed.
    public event Action? Applied;

    /// What the service needs of Windows' input may have changed (<see cref="NeedsTrackpad"/>, the hook): a page started or stopped
    /// showing presses. UI thread. <see cref="Applied"/> says the same after a settings change.
    public event Action? NeedsChanged;

    private readonly HoldCount _pressWatch = new();
    private volatile bool _pressesWatched;

    /// Every real press is wanted while the handle is held (the Mouse and Trackpad page flashes the pressed button's card). UI thread.
    public IDisposable WatchPresses() => _pressWatch.Take();

    private void OnPressWatchChanged()
    {
        _pressesWatched = _pressWatch.Held;
        NeedsChanged?.Invoke();
    }

    /// The hook is needed: a mapping, the autoscroll block, a page showing presses, or an input still held (its release must be seen). Any thread.
    private bool NeedsHook()
    {
        if (_snapshot.NeedsHook || _pressesWatched) return true;
        lock (_gate) return _swallowed.Count > 0 || _undoOnRelease.Count > 0;
    }

    /// The touchpad watcher is needed for the mappings: a gesture has an action, or a page shows presses. Any thread.
    public bool NeedsTrackpad => _snapshot.NeedsTrackpad || _pressesWatched;

    /// Call after any settings change: saves, and hands the hook a fresh copy.
    public void Apply()
    {
        Settings.Save();
        _snapshot = TakeSnapshot();
        ApplyWatchers();
        if (Settings.RunAsAdministrator != _taskFollows) AdministratorMode.FollowSetting(_taskFollows = Settings.RunAsAdministrator, _report);
        Applied?.Invoke();
    }

    private bool _taskFollows; // the "Run as administrator" the sign-in task was last made to follow

    /// Puts <paramref name="target"/> in place of every mapping, layer name and option ("Restore defaults" and its undo), back on layer 1 of
    /// both families. Saved and applied like any change, so a Windows gesture no mapping takes over any more is given back.
    public void ReplaceSettings(MouseSettings target)
    {
        Settings.CopyFrom(target);
        Apply();
        StepLayer(InputFamily.Mouse, 0);
        StepLayer(InputFamily.Trackpad, 0);
        LayersChanged?.Invoke();
    }

    /// Takes mouse.json from disk as it is now (an import wrote it): mappings, layers and options are replaced and applied like any change.
    public bool ReloadFromDisk()
    {
        ReplaceSettings(MouseSettings.Load());
        return true;
    }

    public void RenameLayer(InputFamily family, int layer, string name)
    {
        Settings.LayersOf(family).SetName(layer, name);
        Apply();
        LayersChanged?.Invoke();
    }

    public void ClearLayer(InputFamily family, int layer)
    {
        Settings.LayersOf(family).Clear(layer);
        Apply();
        LayersChanged?.Invoke();
    }

    public void RestoreLayer(InputFamily family, LayerSnapshot snapshot)
    {
        Settings.LayersOf(family).Restore(snapshot);
        Apply();
        LayersChanged?.Invoke();
    }

    public void SetOnlyUsedLayers(InputFamily family, bool on)
    {
        Settings.LayersOf(family).OnlyUsed = on;
        Apply();
        LayersChanged?.Invoke();
    }

    /// Moves <paramref name="family"/>'s layer one step, or back to layer 1 when <paramref name="direction"/> is 0.
    public void StepLayer(InputFamily family, int direction = 1, bool wrap = true)
    {
        _layers.Step(family, direction, wrap);
        NotifyLayer(family);
    }

    /// Makes <paramref name="layer"/> <paramref name="family"/>'s layer in force (the Mouse and Trackpad page's drop-down or "switch to layer"
    /// chose it); the layer shown stays the held one while a "hold for layer" button is down.
    public void SetLayer(InputFamily family, int layer)
    {
        _layers.Choose(family, layer);
        NotifyLayer(family);
    }

    private Snapshot TakeSnapshot() =>
        new(Settings.Bindings.ToDictionary(b => b.Key, b => MouseAction.Parse(b.Value)),
            Settings.ExtraActions.ToDictionary(e => e.Key, e => e.Value.Take(MouseSettings.MaxActions - 1).Select(MouseAction.Parse).ToArray()),
            Enum.GetValues<MouseButton>().Where(b => Settings.ScopeOf(b) is not null)
                .ToDictionary(b => b, b => Settings.ScopeOf(b)!),
            Settings.BlockAutoscroll, Settings.PauseInFullScreen, Settings.NeverActIn is { IsLimited: true } never ? never : null, Settings.SendToRemoteAsKeys);

    private void ApplyWatchers()
    {
        _autoscroll.SetEnabled(_snapshot.WatchesAutoscroll);
        _fullScreen.SetEnabled(Settings.PauseInFullScreen);
    }

    /// Asked by the hook before any handler for every press and wheel step: true while a full-screen app is in front (when paused there) or the
    /// app in front is one mouse features never act in. Reads only what workers have cached, so it never waits.
    private bool PassesEverything()
    {
        var snapshot = _snapshot;
        return (snapshot.PauseInFullScreen && _fullScreen.IsActive) || !_scopes.Allows(snapshot.NeverActIn);
    }

    private void NotifyLayer(InputFamily family)
    {
        var layer = _layers.Current(family);
        Dispatcher.UIThread.Post(() => LayerChanged?.Invoke(family, layer));
    }

    /// Runs on the hook thread for every button, wheel and tilt input. Returns true to swallow it.
    private bool Handle(MouseEvent ev)
    {
        if (!ev.IsDown) return ReleaseSwallowed(ev.Button);
        // A press whose release never arrived (Windows can drop hook calls) first ends what that press started, so held keys,
        // repeats and "hold for layer" can never stay on for good.
        ReleaseSwallowed(ev.Button);
        ButtonPressed?.Invoke(ev.Button);

        // Gestures win over the button's mapping; release, swallowing and "while held" work the same for both.
        var gesture = _gestureAt(ev);
        var snapshot = _snapshot;
        if (gesture is null && ev.Button is MouseButton.Left or MouseButton.Right && OwnProgram.IsUnder(ev.X, ev.Y)) return false;

        var layer = _layers.Current(ev.Button.FamilyOf());
        var key = MouseSettings.KeyOf(layer, ev.Button);
        var action = gesture;
        if (action is null && (!snapshot.Actions.TryGetValue(key, out action) || action.Id == MouseActionCatalog.Default))
            return BlocksAutoscroll(ev, snapshot) || PassThrough(ev, layer, NotMapped);
        // Outside the apps its mapping works in, the input behaves as if it had none.
        if (gesture is null && snapshot.Scopes.TryGetValue(ev.Button, out var scope) && !_scopes.Allows(scope))
            return PassThrough(ev, layer, OutsideApps);
        // Every action of one firing comes from the layer in force when it fired; a layer action among them changes the layer for the next firing.
        var extras = gesture is null ? snapshot.ExtrasOf(key) : NoExtras;
        if (!IsPlainClick(ev.Button))
            _log(gesture is null ? $"{ev.Button} on layer {layer}: {action.Id}{(extras.Length > 0 ? $" and {extras.Length} more" : string.Empty)}" : $"{ev.Button} gesture: {action.Id}");

        var isPressable = ev.Button.HasRelease();
        // "Also send the button's normal click": the original input goes through and is not swallowed on release either.
        // (A trackpad gesture is only copied from raw input and never swallowed; there the same setting keeps Windows' own gesture turned on, see TrackpadService.)
        var keepOriginal = action.Options.Bool(MouseOptions.KeepOriginal);
        // Movement as scroll with its click also sent: an app would start its own autoscroll on the press, so the press is held back and
        // the click is sent on release when the mouse did not move (MouseModes).
        var heldBack = keepOriginal && isPressable && snapshot.BlockAutoscroll && MouseActionCatalog.HoldsClickBack(action);
        var swallow = !keepOriginal || heldBack;
        if (isPressable && swallow) lock (_gate) _swallowed.Add(ev.Button);

        if (action.Id == MouseActionCatalog.Disabled) return true;
        return StartFirst(action, ev, isPressable, extras, heldBack) || swallow;
    }

    /// Starts the input's first action (it may last while the input is held, or run on release or repeatedly), then its other actions
    /// in order. True when the input must be swallowed whatever the action's settings (a layer action).
    private bool StartFirst(MouseAction action, MouseEvent ev, bool isPressable, MouseAction[] extras, bool heldBack)
    {
        var id = action.Id;
        // An input without a release (wheel, tilt, a trackpad gesture) would never end a "while held" action (held keys, a held layer,
        // a held button); the pickers grey them for such inputs, and a mapping saved before that is ignored here.
        if (!isPressable && MouseActionCatalog.IsWhileHeld(id))
        {
            RunInOrder(ev, null, extras);
            return false;
        }
        if (MouseActionCatalog.LayerActionOf(id) is { Move: LayerMove.Hold } held)
        {
            _layers.Hold(ev.Button, held.Family, held.Layer);
            NotifyLayer(held.Family);
            RunInOrder(ev, null, extras);
            return true;
        }
        if (TryChangeLayer(action))
        {
            RunInOrder(ev, null, extras);
            return true;
        }

        if (action.Options.Text(MouseOptions.AlsoSend).Length > 0) Guard.OffUi("Mouse: sending the extra shortcut", () => _runner.SendAlongside(action));

        if (MouseActionCatalog.IsMode(id))
        {
            if (_modes.Start(action, ev, heldBack) is { } undo) lock (_gate) _undoOnRelease[ev.Button] = undo;
            RunInOrder(ev, null, extras);
            return false;
        }

        RunWithTrigger(action, ev, isPressable, extras);
        return false;
    }

    /// "Block apps' middle-click autoscroll": a middle press nothing is mapped to is swallowed (press and release) unless it is over a link, tab,
    /// button or menu item, so the app never starts its autoscroll. Not in the apps the button's scope leaves out; when no ready answer covers the pointer's spot
    /// the press passes through. Nothing is sent on a swallowed press, so nothing can be left held.
    private bool BlocksAutoscroll(MouseEvent ev, Snapshot snapshot)
    {
        if (ev.Button != MouseButton.Middle || !snapshot.BlockAutoscroll) return false;
        if (snapshot.Scopes.TryGetValue(ev.Button, out var scope) && !_scopes.Allows(scope)) return false;
        if (_autoscroll.Verdict(ev.X, ev.Y) != ClickVerdict.Swallow) return false;
        _log("Middle press swallowed: not over a link, tab or button (apps' autoscroll blocked)");
        lock (_gate) _swallowed.Add(ev.Button);
        return true;
    }

    private const string NotMapped = "not mapped", OutsideApps = "not for this app";

    // Plain clicks are left out of the log so they never use up its lines.
    private static bool IsPlainClick(MouseButton button) => button is MouseButton.Left or MouseButton.Right;

    /// An unmapped input keeps its normal Windows behaviour (Back and Forward over OmniControl or the desktop are then stopped by
    /// <see cref="BackForwardGuard"/>, the hook's last handler). The log names the window class each one goes to, so a repeat elsewhere shows where.
    private bool PassThrough(MouseEvent ev, int layer, string why)
    {
        if (!IsPlainClick(ev.Button)) _log($"{ev.Button} on layer {layer}: {why}, passes on over \"{WindowQuery.RootClassAt(ev.X, ev.Y)}\"");
        return false;
    }

    /// The layer actions of either family: next, previous, back to 1, or jump to a layer. False for any other action.
    private bool TryChangeLayer(MouseAction action)
    {
        if (MouseActionCatalog.LayerActionOf(action.Id) is not { } change) return false;
        switch (change.Move)
        {
            case LayerMove.Next: StepLayer(change.Family, 1, action.Options.Bool(MouseOptions.Wrap)); return true;
            case LayerMove.Previous: StepLayer(change.Family, -1, action.Options.Bool(MouseOptions.Wrap)); return true;
            case LayerMove.Reset: StepLayer(change.Family, 0); return true;
            case LayerMove.Switch: SetLayer(change.Family, change.Layer); return true;
            default: return false; // a held layer is handled before
        }
    }

    /// Runs an action when the button is pressed, when it is released, repeatedly while it is held, or (for a shortcut) with its keys held down.
    /// <remarks>The input's other actions follow the first: on release when it runs on release, otherwise on press (a repeat repeats the first only).</remarks>
    private void RunWithTrigger(MouseAction action, MouseEvent ev, bool isPressable, MouseAction[] extras)
    {
        var trigger = isPressable ? action.Options.Text(MouseOptions.Trigger) : MouseOptions.OnPress; // wheel and tilt have no "held" or "released"
        Action? undo = null;

        // Only one combination can be held; a sequence ("Ctrl+K, Ctrl+C") is typed once on the press instead.
        if (trigger == MouseOptions.KeysHeld && action.Id == MouseActionCatalog.Keys && _modes.HoldKeys(action.Param) is { } release)
        {
            undo = release;
            RunInOrder(ev, null, extras);
        }
        else if (isPressable && action.Id == MouseActionCatalog.Omni && _holdOmniAction(action.Param, MouseOptions.RunOf(action.Options)) is { } held)
        {
            undo = held; // an action that lasts while the button is held (repeated clicking): the release ends it
            RunInOrder(ev, null, extras);
        }
        else if (trigger == MouseOptions.OnRelease) undo = () => RunInOrder(ev, action, extras);
        else
        {
            RunInOrder(ev, action, extras);
            if (trigger == MouseOptions.Repeatedly)
                undo = MouseRepeater.Start(() => _runner.Run(action, ev),
                    TimeSpan.FromMilliseconds(action.Options.Int(MouseOptions.RepeatDelay)), TimeSpan.FromMilliseconds(action.Options.Int(MouseOptions.RepeatEvery)));
        }

        if (undo is not null) lock (_gate) _undoOnRelease[ev.Button] = undo;
    }

    /// Runs <paramref name="first"/> (when given) and then the input's other actions, in order, off the hook thread
    /// (<see cref="ActionSequence"/>). While the input's previous run of other actions is still going (a fast wheel), only the first
    /// runs, so at most one sequence per input is ever under way.
    private void RunInOrder(MouseEvent ev, MouseAction? first, MouseAction[] extras)
    {
        var steps = new List<Action>();
        if (first is not null) steps.Add(() => _runner.Run(first, ev));
        bool claimed;
        lock (_gate) claimed = extras.Length > 0 && _sequenceRunning.Add(ev.Button);
        if (claimed) steps.AddRange(extras.Select(extra => (Action)(() => RunLater(extra, ev))));
        if (steps.Count == 0) return;
        Guard.OffUi("Mouse: running actions in order", async () =>
        {
            try
            {
                await ActionSequence.RunAsync(steps, Timing.MouseActionGap, gapFirst: first is null, _log);
            }
            finally
            {
                if (claimed) lock (_gate) _sequenceRunning.Remove(ev.Button);
            }
        });
    }

    /// One of an input's second or later actions: it runs once, at once. A "while held" one (never offered there) is skipped, since
    /// nothing would end it; a toggle mode is flipped, and anything a mode would hold is let go at once.
    private void RunLater(MouseAction action, MouseEvent ev)
    {
        if (!MouseActionCatalog.CanRunLater(action.Id) || TryChangeLayer(action)) return;
        _runner.SendAlongside(action);
        if (MouseActionCatalog.IsMode(action.Id)) _modes.Start(action, ev)?.Invoke();
        else _runner.Run(action, ev);
    }

    private bool ReleaseSwallowed(MouseButton button)
    {
        Action? undo;
        bool swallowed;
        lock (_gate)
        {
            swallowed = _swallowed.Remove(button);
            _undoOnRelease.Remove(button, out undo);
        }
        undo?.Invoke(); // outside the lock: it calls into Windows
        if (_layers.Release(button) is { } released) NotifyLayer(released);
        return swallowed;
    }

    /// The hook was put back after Windows dropped it: a release may have been missed, so every held input is let go now (keys, buttons,
    /// held layers, movement as scroll), as if released. A button still held then simply ends early; pressing it again starts it again.
    private void ReleaseAllHeld() => Guard.Run("Mouse: let go after the hook was put back", () =>
    {
        List<MouseButton> held;
        lock (_gate) held = _undoOnRelease.Keys.Concat(_swallowed).Distinct().ToList();
        foreach (var button in held) ReleaseSwallowed(button);
        foreach (var family in _layers.ReleaseAll()) NotifyLayer(family);
    });

    private void Say(string message) => _report(message);

    public void Dispose()
    {
        _hook.Reinstalled -= ReleaseAllHeld;
        _hook.PointerMoved -= _autoscroll.Moved;
        _autoscroll.Dispose();
        _pressWatch.Changed -= OnPressWatchChanged;
        _modes.BeginClose();
        List<Action> pending;
        lock (_gate) pending = _undoOnRelease.Values.ToList();
        foreach (var undo in pending) undo(); // let go of whatever a held button was doing
        _modes.Dispose();
        Clicking.Dispose();
        ForgetStates();
    }
}
