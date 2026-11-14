using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Mouse;

/// <summary>
/// Remaps the mouse: owns the hook, picks the layer for each input and runs the mapped action with its options.
/// The hook thread only reads an immutable snapshot, so editing settings on the UI thread never blocks or races it.
/// </summary>
public sealed class MouseService : IDisposable
{
    /// <summary>The settings as the hook reads them: the first action per key, and the others in order.</summary>
    private sealed record Snapshot(bool Enabled, Dictionary<string, MouseAction> Actions, Dictionary<string, MouseAction[]> Extras)
    {
        public MouseAction[] ExtrasOf(string key) => Extras.TryGetValue(key, out var extras) ? extras : NoExtras;
    }

    private static readonly MouseAction[] NoExtras = Array.Empty<MouseAction>();

    private readonly MouseActionRunner _runner;
    private readonly MouseModes _modes;
    private readonly Action<string> _report;
    private readonly Action<string> _log;
    private readonly Func<MouseEvent, MouseAction?> _gestureAt;

    private readonly object _gate = new();
    private readonly HashSet<MouseButton> _swallowed = new();           // pressed buttons whose release must be swallowed too
    private readonly Dictionary<MouseButton, Action> _undoOnRelease = new(); // what to do when a button comes up: undo a "while held" action, or run a "when released" one
    private readonly LayerState _layers = new();                        // the mouse's and the trackpad's layer in force
    private readonly HashSet<MouseButton> _sequenceRunning = new();     // inputs whose second and later actions are still running
    private volatile Snapshot _snapshot;

    public MouseSettings Settings { get; } = MouseSettings.Load();

    /// <summary>Raised on the UI thread when a hotkey changed the on/off state.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Raised on the UI thread with a family and its layer now in force, whenever a button, gesture or hotkey changes it (also while a
    /// "hold for layer" button is down).
    /// </summary>
    public event Action<InputFamily, int>? LayerChanged;

    /// <summary>Raised on the UI thread when a layer is renamed, cleared, or the list of layers shown changes.</summary>
    public event Action? LayersChanged;

    /// <summary>The layer of <paramref name="family"/> in force now (a held "hold for layer" button counts).</summary>
    public int LayerOf(InputFamily family) => _layers.Current(family);

    /// <summary>Raised on the hook thread for every real button, wheel or tilt press, mapped or not. Handlers must return at once.</summary>
    public event Action<MouseButton>? ButtonPressed;


    /// <param name="hook">The shared mouse hook (the caller starts and stops it).</param>
    /// <param name="gestureAt">
    /// Asked first on the hook thread for every press: the action a gesture puts on this press instead of the button's mapping (a middle
    /// click over a screen area), or null. That action runs even while remapping is off, exactly as a mapped one would.
    /// </param>
    public MouseService(MouseHook hook, Func<string, bool> runOmniAction, Action<string> report, Func<MouseEvent, MouseAction?> gestureAt)
    {
        _report = report;
        _log = hook.Diagnose;
        _gestureAt = gestureAt;
        _runner = new MouseActionRunner(runOmniAction, Say, hook.Diagnose);
        _modes = new MouseModes(hook);
        _snapshot = TakeSnapshot();
        hook.AddHandler(Handle);
    }

    /// <summary>Raised on the UI thread after <see cref="Apply"/>: a mapping or a switch may have changed.</summary>
    public event Action? Applied;

    /// <summary>Call after any settings change: saves, and hands the hook a fresh copy.</summary>
    public void Apply()
    {
        Settings.Save();
        _snapshot = TakeSnapshot();
        Applied?.Invoke();
    }

    public void SetEnabled(bool on)
    {
        if (Settings.Enabled == on) return;
        Settings.Enabled = on;
        Apply();
        StateChanged?.Invoke();
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

    public void SetOnlyUsedLayers(InputFamily family, bool on)
    {
        Settings.LayersOf(family).OnlyUsed = on;
        Apply();
        LayersChanged?.Invoke();
    }

    /// <summary>Moves <paramref name="family"/>'s layer one step, or back to layer 1 when <paramref name="direction"/> is 0.</summary>
    public void StepLayer(InputFamily family, int direction = 1, bool wrap = true)
    {
        _layers.Step(family, direction, wrap);
        NotifyLayer(family);
    }

    /// <summary>
    /// Makes <paramref name="layer"/> <paramref name="family"/>'s layer in force (the Mouse and Trackpad page's drop-down or "switch to layer"
    /// chose it); the layer shown stays the held one while a "hold for layer" button is down.
    /// </summary>
    public void SetLayer(InputFamily family, int layer)
    {
        _layers.Choose(family, layer);
        NotifyLayer(family);
    }

    private Snapshot TakeSnapshot() =>
        new(Settings.Enabled, Settings.Bindings.ToDictionary(b => b.Key, b => MouseAction.Parse(b.Value)),
            Settings.ExtraActions.ToDictionary(e => e.Key, e => e.Value.Take(MouseSettings.MaxActions - 1).Select(MouseAction.Parse).ToArray()));

    private void NotifyLayer(InputFamily family)
    {
        var layer = _layers.Current(family);
        Dispatcher.UIThread.Post(() => LayerChanged?.Invoke(family, layer));
    }

    /// <summary>Runs on the hook thread for every button, wheel and tilt input. Returns true to swallow it.</summary>
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
        if (gesture is null && !snapshot.Enabled) return false;
        if (gesture is null && ev.Button is MouseButton.Left or MouseButton.Right && OwnProgram.IsUnder(ev.X, ev.Y)) return false;

        var layer = _layers.Current(ev.Button.FamilyOf());
        var key = MouseSettings.KeyOf(layer, ev.Button);
        var action = gesture;
        if (action is null && (!snapshot.Actions.TryGetValue(key, out action) || action.Id == MouseActionCatalog.Default))
            return PassThrough(ev, layer);
        // Every action of one firing comes from the layer in force when it fired; a layer action among them changes the layer for the next firing.
        var extras = gesture is null ? snapshot.ExtrasOf(key) : NoExtras;
        if (!IsPlainClick(ev.Button))
            _log(gesture is null ? $"{ev.Button} on layer {layer}: {action.Id}{(extras.Length > 0 ? $" and {extras.Length} more" : string.Empty)}" : $"{ev.Button} gesture: {action.Id}");

        var isPressable = ev.Button.HasRelease();
        // "Also send the button's normal click": the original input goes through and is not swallowed on release either.
        // (A trackpad gesture is only copied from raw input and never swallowed; there the same setting keeps Windows' own gesture turned on, see TrackpadService.)
        var swallow = !action.Options.Bool(MouseOptions.KeepOriginal);
        if (isPressable && swallow) lock (_gate) _swallowed.Add(ev.Button);

        if (action.Id == MouseActionCatalog.Disabled) return true;
        return StartFirst(action, ev, isPressable, extras) || swallow;
    }

    /// <summary>
    /// Starts the input's first action (it may last while the input is held, or run on release or repeatedly), then its other actions
    /// in order. True when the input must be swallowed whatever the action's settings (a layer action).
    /// </summary>
    private bool StartFirst(MouseAction action, MouseEvent ev, bool isPressable, MouseAction[] extras)
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

        if (action.Options.Text(MouseOptions.AlsoSend).Length > 0) Task.Run(() => _runner.SendAlongside(action));

        if (MouseActionCatalog.IsMode(id))
        {
            if (_modes.Start(action, ev) is { } undo) lock (_gate) _undoOnRelease[ev.Button] = undo;
            RunInOrder(ev, null, extras);
            return false;
        }

        RunWithTrigger(action, ev, isPressable, extras);
        return false;
    }

    // Plain clicks are left out of the log so they never use up its lines.
    private static bool IsPlainClick(MouseButton button) => button is MouseButton.Left or MouseButton.Right;

    /// <summary>
    /// An unmapped input keeps its normal Windows behaviour, except Back and Forward over OmniControl's own windows or the desktop: nothing
    /// there can go back or forward, and Windows hands an unused side click on to the shell as a "browser back/forward" command. On 12 Nov
    /// each pass-through Back started a new Explorer folder-window process until the shell crashed. There the press and its release are
    /// swallowed. The log names the window class each pass-through goes to, so a repeat elsewhere shows where.
    /// </summary>
    private bool PassThrough(MouseEvent ev, int layer)
    {
        if (IsPlainClick(ev.Button)) return false;
        var goesTo = WindowQuery.RootClassAt(ev.X, ev.Y);
        var nowhereToGo = ev.Button is MouseButton.Back or MouseButton.Forward && (OwnProgram.IsUnder(ev.X, ev.Y) || PointerArea.IsDesktop(ev.X, ev.Y));
        _log($"{ev.Button} on layer {layer}: not mapped, {(nowhereToGo ? "swallowed (no back or forward there)" : "passes through")} over \"{goesTo}\"");
        if (!nowhereToGo) return false;
        lock (_gate) _swallowed.Add(ev.Button);
        return true;
    }

    /// <summary>The layer actions of either family: next, previous, back to 1, or jump to a layer. False for any other action.</summary>
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

    /// <summary>Runs an action when the button is pressed, when it is released, repeatedly while it is held, or (for a shortcut) with its keys held down.</summary>
    /// <remarks>The input's other actions follow the first: on release when it runs on release, otherwise on press (a repeat repeats the first only).</remarks>
    private void RunWithTrigger(MouseAction action, MouseEvent ev, bool isPressable, MouseAction[] extras)
    {
        var trigger = isPressable ? action.Options.Text(MouseOptions.Trigger) : MouseOptions.OnPress; // wheel and tilt have no "held" or "released"
        Action? undo = null;

        if (trigger == MouseOptions.KeysHeld && action.Id == MouseActionCatalog.Keys)
        {
            undo = _modes.HoldKeys(action.Param);
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

    /// <summary>
    /// Runs <paramref name="first"/> (when given) and then the input's other actions, in order, off the hook thread
    /// (<see cref="ActionSequence"/>). While the input's previous run of other actions is still going (a fast wheel), only the first
    /// runs, so at most one sequence per input is ever under way.
    /// </summary>
    private void RunInOrder(MouseEvent ev, MouseAction? first, MouseAction[] extras)
    {
        var steps = new List<Action>();
        if (first is not null) steps.Add(() => _runner.Run(first, ev));
        bool claimed;
        lock (_gate) claimed = extras.Length > 0 && _sequenceRunning.Add(ev.Button);
        if (claimed) steps.AddRange(extras.Select(extra => (Action)(() => RunLater(extra, ev))));
        if (steps.Count == 0) return;
        Task.Run(async () =>
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

    /// <summary>
    /// One of an input's second or later actions: it runs once, at once. A "while held" one (never offered there) is skipped, since
    /// nothing would end it; a toggle mode is flipped, and anything a mode would hold is let go at once.
    /// </summary>
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

    private void Say(string message) => Dispatcher.UIThread.Post(() => _report(message));

    public void Dispose()
    {
        List<Action> pending;
        lock (_gate) pending = _undoOnRelease.Values.ToList();
        foreach (var undo in pending) undo(); // let go of whatever a held button was doing
        _modes.Dispose();
    }
}
