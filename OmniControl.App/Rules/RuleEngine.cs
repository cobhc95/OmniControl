using OmniControl.App.Mouse;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Rules;

/// <summary>
/// Watches the mouse hook for the rules that are on. Runs inside the hook (asked before the gestures and the mouse mappings), so it
/// only decides and records: an input no rule uses costs one array lookup; a matching input is taken (its press and release, or the
/// scroll) and the rule's steps are handed to a worker. The hook thread reads an immutable snapshot (<see cref="Load"/>).
/// </summary>
internal sealed class RuleEngine : IDisposable
{
    /// <summary>The rules of one moment, per button; empty arrays where no rule listens.</summary>
    private sealed record Snapshot(bool Enabled, int EdgeStrip, CompiledRule[][] ByButton, int DoubleMs, int DoubleWidth, int DoubleHeight);

    /// <summary>A press-and-hold trigger waiting for its time: a release before it gives the click back, the timer firing runs the rule.</summary>
    private sealed class PendingHold : IDisposable
    {
        private const int Waiting = 0, Fired = 1, Cancelled = 2;
        private int _state;
        public Timer? Timer { get; set; }

        public bool TryFire() => Interlocked.CompareExchange(ref _state, Fired, Waiting) == Waiting;
        public bool TryCancel() => Interlocked.CompareExchange(ref _state, Cancelled, Waiting) == Waiting;
        public void Dispose() => Timer?.Dispose();
    }

    private static readonly int ButtonCount = Enum.GetValues<MouseButton>().Length;
    private static readonly CompiledRule[][] NoRules = Enumerable.Range(0, ButtonCount).Select(_ => Array.Empty<CompiledRule>()).ToArray();

    private readonly Func<InputFamily, int> _layer;
    private readonly Action<CompiledRule, MouseEvent> _run;
    private readonly ProgramNames _names = new();
    private readonly bool[] _swallowed = new bool[ButtonCount];        // hook thread only: presses whose release is taken too
    private readonly PendingHold?[] _holds = new PendingHold?[ButtonCount];
    private volatile Snapshot _snapshot = new(false, 0, NoRules, 0, 0, 0);

    // The last left press, for double-click triggers (hook thread only).
    private long _lastLeftAt = long.MinValue / 2;
    private int _lastLeftX, _lastLeftY;

    /// <param name="hook">The shared mouse hook; the engine's handler is put ahead of every other.</param>
    /// <param name="layer">The layer of a family (mouse or trackpad) in force now.</param>
    /// <param name="run">Starts a matching rule's steps (called on the hook or hotkey thread; it must hand the work to a worker).</param>
    public RuleEngine(MouseHook hook, Func<InputFamily, int> layer, Action<CompiledRule, MouseEvent> run)
    {
        _layer = layer;
        _run = run;
        hook.AddHandler(Handle, first: true);
    }

    /// <summary>Hands the hook a fresh copy of the rules that are on (<paramref name="enabled"/> false: none).</summary>
    public void Load(IEnumerable<CompiledRule> rules, bool enabled, int edgeStrip)
    {
        var byButton = Enumerable.Range(0, ButtonCount)
            .Select(b => rules.Where(r => r.Trigger.Kind != TriggerKind.Shortcut && r.Trigger.Buttons.Any(t => (int)t == b)).ToArray()).ToArray();
        var (ms, width, height) = SystemCommands.DoubleClick();
        _snapshot = new Snapshot(enabled, edgeStrip, byButton, ms, width, height);
    }

    /// <summary>A rule's shortcut was pressed (on the hotkey thread): runs it when its layer and conditions hold, with the pointer where it is.</summary>
    public void OnShortcut(CompiledRule rule)
    {
        var snapshot = _snapshot;
        if (!snapshot.Enabled || OwnProgram.IsActive()) return;
        var (x, y) = SystemCommands.GetCursorPos() ?? (0, 0);
        var probe = new RuleProbe(x, y, underPointer: false);
        if (rule.Applies(ref probe, _layer, snapshot.EdgeStrip, _names)) _run(rule, new MouseEvent(MouseButton.Left, true, x, y));
    }

    /// <summary>Runs on the hook thread for every button, wheel and tilt input. Returns true to take it.</summary>
    private bool Handle(MouseEvent ev)
    {
        var button = (int)ev.Button;
        if (!ev.IsDown) return Released(button);
        ForgetPress(button); // a press whose release never arrived (Windows can drop hook calls) leaves nothing behind

        var snapshot = _snapshot;
        var rules = snapshot.ByButton[button];
        if (!snapshot.Enabled || rules.Length == 0) return false;
        if (ev.Button == MouseButton.Left && !IsSecondClick(ev, snapshot)) return false; // left only listens for double clicks
        if (OwnProgram.IsUnder(ev.X, ev.Y) || OwnProgram.IsActive()) return false; // OmniControl's own window is never affected

        var probe = new RuleProbe(ev.X, ev.Y, underPointer: true);
        foreach (var rule in rules)
        {
            if (!rule.Applies(ref probe, _layer, snapshot.EdgeStrip, _names)) continue;
            Take(rule, ev);
            return true;
        }
        return false;
    }

    /// <summary>True for the second of two left presses within Windows' double-click time and size; the pair is then used up.</summary>
    private bool IsSecondClick(MouseEvent ev, Snapshot snapshot)
    {
        var now = Environment.TickCount64;
        var second = now - _lastLeftAt <= snapshot.DoubleMs
            && Math.Abs(ev.X - _lastLeftX) <= snapshot.DoubleWidth / 2 && Math.Abs(ev.Y - _lastLeftY) <= snapshot.DoubleHeight / 2;
        _lastLeftAt = second ? long.MinValue / 2 : now;
        _lastLeftX = ev.X;
        _lastLeftY = ev.Y;
        return second;
    }

    private void Take(CompiledRule rule, MouseEvent ev)
    {
        var button = (int)ev.Button;
        if (ev.Button.HasRelease()) _swallowed[button] = true;
        if (rule.Trigger.Kind != TriggerKind.Hold)
        {
            _run(rule, ev);
            return;
        }
        var hold = new PendingHold();
        hold.Timer = new Timer(_ =>
        {
            if (hold.TryFire()) _run(rule, ev);
        }, null, rule.HoldMs, Timeout.Infinite);
        Interlocked.Exchange(ref _holds[button], hold)?.Dispose();
    }

    /// <summary>A release: taken when its press was. A release before a hold's time gives the click back (sent from a worker, never inside the hook).</summary>
    private bool Released(int button)
    {
        if (Interlocked.Exchange(ref _holds[button], null) is { } hold)
        {
            hold.Dispose();
            if (hold.TryCancel()) Task.Run(() => InputSender.Click((MouseButton)button));
        }
        var taken = _swallowed[button];
        _swallowed[button] = false;
        return taken;
    }

    private void ForgetPress(int button)
    {
        _swallowed[button] = false;
        if (Interlocked.Exchange(ref _holds[button], null) is { } hold)
        {
            hold.TryCancel();
            hold.Dispose();
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < _holds.Length; i++)
            if (Interlocked.Exchange(ref _holds[i], null) is { } hold)
            {
                hold.TryCancel();
                hold.Dispose();
            }
    }
}
