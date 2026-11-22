using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// What runs for one count of presses: the button's first action (null when it belongs to another count or runs at once) and the others.
internal readonly record struct PressStep(MouseAction? First, MouseAction[] Extras)
{
    public static readonly PressStep None = new(null, Array.Empty<MouseAction>());

    public bool IsEmpty => First is null && Extras.Length == 0;
}

/// How a button's actions split when some run "When the button is held" or "pressed twice / three times": the held ones run once the
/// button has been down for <see cref="HoldFor"/>; the others wait for the release and run for the number of presses counted (a press
/// with no action of its own count runs the nearest lower count's). When the first action is not one that runs on the press, when held
/// or on a count (released, repeated, keys held, a mode or a layer), it and the pressed extras run as before and only the held and
/// counted extras wait (<see cref="DefersFirst"/> false).
internal sealed record HoldSplit(MouseAction? HeldFirst, MouseAction[] HeldExtras, MouseAction[] ShortExtras, bool DefersFirst, TimeSpan HoldFor,
    PressStep[] Steps, TimeSpan Within)
{
    private const int MaxPresses = 3;

    public bool HasHeld => HeldFirst is not null || HeldExtras.Length > 0;

    /// The most presses any action waits for (1 when none counts presses).
    public int MaxCount => Math.Max(1, Array.FindLastIndex(Steps, s => !s.IsEmpty) + 1);

    public bool IsCounted => MaxCount > 1;

    /// What runs after <paramref name="count"/> presses: that count's actions, else the nearest lower count's.
    public PressStep StepFor(int count)
    {
        for (var at = Math.Min(count, Steps.Length) - 1; at >= 0; at--)
            if (!Steps[at].IsEmpty) return Steps[at];
        return PressStep.None;
    }

    /// The split of a button's first action and its other actions; null when none runs when held or on a count (the button works as it always did).
    public static HoldSplit? Of(MouseAction first, MouseAction[] extras)
    {
        var firstHeld = MouseOptions.RunsWhenHeld(first.Options);
        var firstPresses = MouseOptions.PressesOf(first.Options);
        var heldExtras = extras.Where(e => MouseOptions.RunsWhenHeld(e.Options)).ToArray();
        var countedExtras = extras.Where(e => MouseOptions.PressesOf(e.Options) > 0).ToArray();
        if (!firstHeld && firstPresses == 0 && heldExtras.Length == 0 && countedExtras.Length == 0) return null;

        var defers = firstHeld || firstPresses > 0 || (first.Options.Text(MouseOptions.Trigger) == MouseOptions.OnPress && RunsOnce(first.Id));
        var shortExtras = extras.Except(heldExtras).Except(countedExtras).ToArray();
        var steps = Enumerable.Repeat(PressStep.None, MaxPresses).ToArray();
        if (defers) steps[0] = new PressStep(!firstHeld && firstPresses == 0 ? first : null, shortExtras); // else they already ran with the press
        for (var count = 2; count <= MaxPresses; count++)
            steps[count - 1] = new PressStep(firstPresses == count ? first : null, countedExtras.Where(e => MouseOptions.PressesOf(e.Options) == count).ToArray());

        var holder = firstHeld ? first : heldExtras.FirstOrDefault();
        var counter = firstPresses > 0 ? first : countedExtras.FirstOrDefault();
        return new HoldSplit(firstHeld ? first : null, heldExtras, shortExtras, defers,
            holder is null ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(holder.Options.Int(MouseOptions.HoldFor)), steps,
            counter is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(counter.Options.Int(MouseOptions.PressesWithin)));
    }

    // Modes, layers, "while held" actions and Disabled act on the press itself, so they are never held back for a short press.
    private static bool RunsOnce(string id) =>
        id != MouseActionCatalog.Disabled && !MouseActionCatalog.IsMode(id) && MouseActionCatalog.LayerActionOf(id) is null && !MouseActionCatalog.IsWhileHeld(id);
}

/// One hold timer per pressed button: on release before the time the short-press step runs, once the time is up the held step runs
/// (and the release then does nothing). The timers run off the hook thread; the steps only queue work.
internal sealed class HoldWatch : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<MouseButton, (ITimer Timer, Action Short)> _pending = new();

    public HoldWatch(TimeProvider clock) => _clock = clock;

    /// Starts timing <paramref name="button"/>'s press (a press still timed is dropped first).
    public void Start(MouseButton button, TimeSpan holdFor, Action onShort, Action onHeld)
    {
        Cancel(button);
        ITimer? timer = null;
        timer = _clock.CreateTimer(_ =>
        {
            lock (_gate)
            {
                if (!_pending.TryGetValue(button, out var pending) || pending.Timer != timer) return;
                _pending.Remove(button);
            }
            timer!.Dispose();
            Guard.Run("Mouse: a held button's actions", onHeld);
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        lock (_gate) _pending[button] = (timer, onShort);
        timer.Change(holdFor, Timeout.InfiniteTimeSpan); // only once it is listed, so an early tick always finds it
    }

    /// The button came up: true (and the short-press step run) when it was released before its hold time.
    public bool Release(MouseButton button)
    {
        if (Take(button) is not { } onShort) return false;
        Guard.Run("Mouse: a short press's actions", onShort);
        return true;
    }

    /// Forgets the press without running anything (a missed release, the hook put back).
    public void Cancel(MouseButton button) => Take(button);

    public void CancelAll()
    {
        List<MouseButton> buttons;
        lock (_gate) buttons = _pending.Keys.ToList();
        foreach (var button in buttons) Cancel(button);
    }

    private Action? Take(MouseButton button)
    {
        (ITimer Timer, Action Short) pending;
        lock (_gate)
            if (!_pending.Remove(button, out pending)) return null;
        pending.Timer.Dispose();
        return pending.Short;
    }

    public void Dispose() => CancelAll();
}
