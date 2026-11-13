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
    private sealed record Snapshot(bool Enabled, Dictionary<string, MouseAction> Actions);

    private readonly MouseActionRunner _runner;
    private readonly MouseModes _modes;
    private readonly Action<string> _report;
    private readonly Action<string> _log;
    private readonly Func<MouseEvent, MouseAction?> _gestureAt;

    private readonly object _gate = new();
    private readonly HashSet<MouseButton> _swallowed = new();           // pressed buttons whose release must be swallowed too
    private readonly Dictionary<MouseButton, int> _heldLayer = new();   // "hold for layer N" buttons currently down
    private readonly Dictionary<MouseButton, Action> _undoOnRelease = new(); // what to do when a button comes up: undo a "while held" action, or run a "when released" one
    private int _baseLayer = 1;                                         // the layer chosen by "next layer"
    private volatile Snapshot _snapshot;

    public MouseSettings Settings { get; } = MouseSettings.Load();

    /// <summary>Raised on the UI thread when a hotkey changed the on/off state.</summary>
    public event Action? StateChanged;

    /// <summary>Raised on the UI thread with the layer now in force, whenever a button, gesture or hotkey changes it (also while a "hold for layer" button is down).</summary>
    public event Action<int>? LayerChanged;

    /// <summary>Raised on the UI thread when a layer is renamed, cleared, or the list of layers shown changes.</summary>
    public event Action? LayersChanged;

    /// <summary>The layer in force now (a held "hold for layer" button counts).</summary>
    public int Layer => CurrentLayer();

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

    /// <summary>Call after any settings change: saves, and hands the hook a fresh copy.</summary>
    public void Apply()
    {
        Settings.Save();
        _snapshot = TakeSnapshot();
    }

    public void SetEnabled(bool on)
    {
        if (Settings.Enabled == on) return;
        Settings.Enabled = on;
        Apply();
        StateChanged?.Invoke();
    }

    public void RenameLayer(int layer, string name)
    {
        Settings.SetName(layer, name);
        Apply();
        LayersChanged?.Invoke();
    }

    public void ClearLayer(int layer)
    {
        Settings.ClearLayer(layer);
        Apply();
        LayersChanged?.Invoke();
    }

    public void SetOnlyUsedLayers(bool on)
    {
        Settings.OnlyUsedLayers = on;
        Apply();
        LayersChanged?.Invoke();
    }

    /// <summary>Moves the layer one step, or back to layer 1 when <paramref name="direction"/> is 0.</summary>
    public void StepLayer(int direction = 1, bool wrap = true)
    {
        lock (_gate)
        {
            var next = _baseLayer + direction;
            _baseLayer = direction == 0 ? 1
                : wrap ? Services.Wrap.Index(next - 1, MouseSettings.Layers) + 1 // layers count from 1
                : Math.Clamp(next, 1, MouseSettings.Layers);
        }
        NotifyLayer();
    }

    /// <summary>Makes <paramref name="layer"/> the layer in force (the Mouse and Trackpad page's drop-down chose it); the layer shown stays the held one while a "hold for layer" button is down.</summary>
    public void SetLayer(int layer) => SwitchToLayer(layer);

    private void SwitchToLayer(int layer)
    {
        lock (_gate) _baseLayer = Math.Clamp(layer, 1, MouseSettings.Layers);
        NotifyLayer();
    }

    private Snapshot TakeSnapshot() =>
        new(Settings.Enabled, Settings.Bindings.ToDictionary(b => b.Key, b => MouseAction.Parse(b.Value)));

    private void NotifyLayer()
    {
        var layer = CurrentLayer();
        Dispatcher.UIThread.Post(() => LayerChanged?.Invoke(layer));
    }

    private int CurrentLayer()
    {
        lock (_gate) return _heldLayer.Count > 0 ? _heldLayer.Values.Last() : _baseLayer;
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

        var layer = CurrentLayer();
        var action = gesture;
        if (action is null && (!snapshot.Actions.TryGetValue(MouseSettings.KeyOf(layer, ev.Button), out action) || action.Id == MouseActionCatalog.Default))
            return PassThrough(ev, layer);
        if (!IsPlainClick(ev.Button)) _log(gesture is null ? $"{ev.Button} on layer {layer}: {action.Id}" : $"{ev.Button} gesture: {action.Id}");

        var isPressable = ev.Button.HasRelease();
        // "Also send the button's normal click": the original input goes through and is not swallowed on release either.
        var swallow = !action.Options.Bool(MouseOptions.KeepOriginal);
        if (isPressable && swallow) lock (_gate) _swallowed.Add(ev.Button);

        var id = action.Id;
        if (id == MouseActionCatalog.Disabled) return true;
        // An input without a release (wheel, tilt, a trackpad gesture) would never end a "while held" action (held keys, a held layer,
        // a held button); the pickers grey them for such inputs, and a mapping saved before that is ignored here.
        if (!isPressable && MouseActionCatalog.IsWhileHeld(id)) return swallow;
        if (MouseActionCatalog.HeldLayer(id) is { } held)
        {
            lock (_gate) _heldLayer[ev.Button] = held;
            NotifyLayer();
            return true;
        }
        if (TryChangeLayer(action)) return true;

        if (action.Options.Text(MouseOptions.AlsoSend).Length > 0) Task.Run(() => _runner.SendAlongside(action));

        if (MouseActionCatalog.IsMode(id))
        {
            if (_modes.Start(action, ev) is { } undo) lock (_gate) _undoOnRelease[ev.Button] = undo;
            return swallow;
        }

        RunWithTrigger(action, ev, isPressable);
        return swallow;
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

    /// <summary>The layer actions: next, previous, back to 1, or jump to a layer. False for any other action.</summary>
    private bool TryChangeLayer(MouseAction action)
    {
        switch (action.Id)
        {
            case MouseActionCatalog.NextLayer: StepLayer(1, action.Options.Bool(MouseOptions.Wrap)); return true;
            case MouseActionCatalog.PreviousLayer: StepLayer(-1, action.Options.Bool(MouseOptions.Wrap)); return true;
            case MouseActionCatalog.ResetLayer: StepLayer(0); return true;
        }
        if (MouseActionCatalog.SwitchedLayer(action.Id) is not { } target) return false;
        SwitchToLayer(target);
        return true;
    }

    /// <summary>Runs an action when the button is pressed, when it is released, repeatedly while it is held, or (for a shortcut) with its keys held down.</summary>
    private void RunWithTrigger(MouseAction action, MouseEvent ev, bool isPressable)
    {
        var trigger = isPressable ? action.Options.Text(MouseOptions.Trigger) : MouseOptions.OnPress; // wheel and tilt have no "held" or "released"
        Action? undo = null;

        if (trigger == MouseOptions.KeysHeld && action.Id == MouseActionCatalog.Keys) undo = _modes.HoldKeys(action.Param);
        else if (trigger == MouseOptions.OnRelease) undo = () => Task.Run(() => _runner.Run(action, ev));
        else
        {
            Task.Run(() => _runner.Run(action, ev));
            if (trigger == MouseOptions.Repeatedly)
                undo = MouseRepeater.Start(() => _runner.Run(action, ev),
                    TimeSpan.FromMilliseconds(action.Options.Int(MouseOptions.RepeatDelay)), TimeSpan.FromMilliseconds(action.Options.Int(MouseOptions.RepeatEvery)));
        }

        if (undo is not null) lock (_gate) _undoOnRelease[ev.Button] = undo;
    }

    private bool ReleaseSwallowed(MouseButton button)
    {
        Action? undo;
        bool swallowed, layerReleased;
        lock (_gate)
        {
            layerReleased = _heldLayer.Remove(button);
            swallowed = _swallowed.Remove(button);
            _undoOnRelease.Remove(button, out undo);
        }
        undo?.Invoke(); // outside the lock: it calls into Windows
        if (layerReleased) NotifyLayer();
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
