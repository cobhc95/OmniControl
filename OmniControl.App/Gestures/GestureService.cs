using OmniControl.App.Services;
using Avalonia.Threading;
using OmniControl.App.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Gestures;

/// <summary>
/// Turns mouse-wheel scrolling into actions: over a screen area (taskbar, edges, title bar, desktop) or while keys are held.
/// A gesture that is on swallows the scroll, so the window under the pointer does not also scroll.
/// Middle-click gestures are only looked up here (<see cref="MiddleClickAt"/>); <see cref="MouseService"/> runs them like a button mapping.
/// The hook thread reads an immutable snapshot, so editing settings on the UI thread never blocks or races it.
/// </summary>
public sealed class GestureService
{
    // Middle-click gestures have no edge areas, so a strip of 0 keeps an edge from hiding the desktop or title bar beneath it.
    private const int NoEdgeStrip = 0;

    private sealed record Active(string Step, OptionValues Options);
    private sealed record Snapshot(bool Enabled, int EdgeStrip, Dictionary<Modifiers, Active> Keys, Dictionary<PointerAreaKind, Active> Areas,
        Dictionary<PointerAreaKind, MouseAction> MiddleAreas);

    private readonly GestureActionRunner _runner;
    private volatile Snapshot _snapshot;

    public GestureSettings Settings { get; } = GestureSettings.Load();

    /// <param name="hook">The shared mouse hook (the caller starts and stops it).</param>
    /// <param name="runLevel">Moves an OmniControl level (by its id) up or down by a percent; false when no action moves it.</param>
    public GestureService(MouseHook hook, Func<string, bool, int, bool> runLevel, Action<string> report)
    {
        _runner = new GestureActionRunner(runLevel, message => Dispatcher.UIThread.Post(() => report(message)));
        _snapshot = TakeSnapshot();
        hook.AddHandler(Handle);
    }

    /// <summary>Raised after every settings change (the custom rules follow the on/off switch and the edge strip).</summary>
    public event Action? Applied;

    /// <summary>Call after any settings change: saves, and hands the hook a fresh copy.</summary>
    public void Apply()
    {
        Settings.Save();
        _snapshot = TakeSnapshot();
        Applied?.Invoke();
    }

    private Snapshot TakeSnapshot()
    {
        var on = GestureCatalog.Triggers.Where(t => Settings.RuleFor(t).On).ToList();
        Active ActiveFor(GestureTrigger t)
        {
            var rule = Settings.RuleFor(t);
            return new Active(rule.Action, new OptionValues(new Dictionary<string, string>(rule.Options)));
        }
        MouseAction ClickFor(GestureTrigger t)
        {
            var rule = Settings.RuleFor(t);
            return MouseAction.From(rule.Action, rule.Options);
        }

        var wheel = on.Where(t => !t.MiddleClick).ToList();
        return new Snapshot(
            Settings.Enabled,
            Settings.ClampedEdgeStrip(),
            wheel.Where(t => t.Keys != Modifiers.None).ToDictionary(t => t.Keys, ActiveFor),
            wheel.Where(t => t.Area != PointerAreaKind.Elsewhere).ToDictionary(t => t.Area, ActiveFor),
            on.Where(t => t.MiddleClick).ToDictionary(t => t.Area, ClickFor));
    }

    /// <summary>
    /// Runs on the hook thread for every button press: the action of the middle-click gesture that is on for the area under the pointer,
    /// or null (not a middle press, gestures off, no such gesture there, or a modifier key held). Costs nothing while no middle-click gesture is on.
    /// </summary>
    public MouseAction? MiddleClickAt(MouseEvent ev)
    {
        var snapshot = _snapshot;
        if (ev.Button != MouseButton.Middle || !ev.IsDown || !snapshot.Enabled || snapshot.MiddleAreas.Count == 0) return null;
        // As for the wheel: areas only count with no modifier held, so Ctrl+middle click keeps its own meaning.
        if (KeyboardState.Held() != Modifiers.None) return null;
        return snapshot.MiddleAreas.TryGetValue(PointerArea.At(ev.X, ev.Y, NoEdgeStrip), out var action) ? action : null;
    }

    /// <summary>Runs on the hook thread for every input; only the wheel matters here. Returns true to swallow it.</summary>
    private bool Handle(MouseEvent ev)
    {
        var snapshot = _snapshot;
        if (!snapshot.Enabled || ev.Button is not (MouseButton.WheelUp or MouseButton.WheelDown)) return false;
        if (snapshot.Keys.Count == 0 && snapshot.Areas.Count == 0) return false;

        // Keys held decide first; areas only count when no modifier is held, so Ctrl+wheel zoom over the taskbar stays Ctrl+wheel.
        var held = KeyboardState.Held();
        Active? active = null;
        if (held != Modifiers.None) snapshot.Keys.TryGetValue(held, out active);
        else if (snapshot.Areas.Count > 0) snapshot.Areas.TryGetValue(PointerArea.At(ev.X, ev.Y, snapshot.EdgeStrip), out active);
        if (active is null) return false;

        var wheelUp = ev.Button == MouseButton.WheelUp;
        Task.Run(() => _runner.Run(active.Step, active.Options, wheelUp, ev));
        return true;
    }
}
