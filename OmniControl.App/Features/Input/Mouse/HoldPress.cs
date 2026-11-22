using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// How a button's actions split when some run "When the button is held": the held ones run once the button has been down for
/// <see cref="HoldFor"/>; the pressed ones wait for the release and run only on a short press. When the first action is not one that
/// runs on the press or when held (released, repeated, keys held, a mode or a layer), it and the pressed extras run as before and only
/// the held extras wait (<see cref="DefersFirst"/> false).
internal sealed record HoldSplit(MouseAction? HeldFirst, MouseAction[] HeldExtras, MouseAction[] ShortExtras, bool DefersFirst, TimeSpan HoldFor)
{
    /// The split of a button's first action and its other actions; null when none runs when held (the button works as it always did).
    public static HoldSplit? Of(MouseAction first, MouseAction[] extras)
    {
        var firstHeld = MouseOptions.RunsWhenHeld(first.Options);
        var heldExtras = extras.Where(e => MouseOptions.RunsWhenHeld(e.Options)).ToArray();
        if (!firstHeld && heldExtras.Length == 0) return null;
        var holder = firstHeld ? first : heldExtras[0];
        var defers = firstHeld || (first.Options.Text(MouseOptions.Trigger) == MouseOptions.OnPress && RunsOnce(first.Id));
        return new HoldSplit(firstHeld ? first : null, heldExtras, extras.Except(heldExtras).ToArray(), defers,
            TimeSpan.FromMilliseconds(holder.Options.Int(MouseOptions.HoldFor)));
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
