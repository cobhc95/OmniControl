using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// Counts a button's short presses for "pressed twice / three times": after each release it waits up to the allowed pause for another
/// press; at the most presses anything waits for, or when the pause runs out, the count is reported once. A press that goes down in time
/// keeps the earlier count (its timer stops) until it is released. The timers run off the hook thread; the report only queues work.
internal sealed class PressCounter : IDisposable
{
    private sealed class Pending(int count, ITimer? timer, Action<int> fire)
    {
        public int Count { get; } = count;
        public ITimer? Timer { get; } = timer;
        public Action<int> Fire { get; } = fire;
    }

    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<MouseButton, Pending> _pending = new();

    public PressCounter(TimeProvider clock) => _clock = clock;

    /// The button went down: earlier presses still waiting for it stop their timer and are kept until it comes up.
    public void Down(MouseButton button)
    {
        lock (_gate)
            if (_pending.TryGetValue(button, out var pending))
            {
                pending.Timer?.Dispose();
                _pending[button] = new Pending(pending.Count, null, pending.Fire);
            }
    }

    /// A short press ended: it is counted with the earlier ones, and <paramref name="report"/> gets the count at <paramref name="most"/>
    /// presses at once, or after <paramref name="within"/> without another press.
    public void Up(MouseButton button, int most, TimeSpan within, Action<int> report)
    {
        int count;
        lock (_gate)
        {
            count = (_pending.Remove(button, out var earlier) ? earlier.Count : 0) + 1;
            earlier?.Timer?.Dispose();
            if (count < most)
            {
                ITimer? timer = null;
                timer = _clock.CreateTimer(_ => Expired(button, timer!), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _pending[button] = new Pending(count, timer, report);
                timer.Change(within, Timeout.InfiniteTimeSpan); // only once it is listed, so an early tick always finds it
                return;
            }
        }
        Guard.Run("Mouse: a press count's actions", () => report(count));
    }

    /// The earlier presses are reported now (the button that followed them was held).
    public void Flush(MouseButton button)
    {
        Pending? pending;
        lock (_gate) _pending.Remove(button, out pending);
        pending?.Timer?.Dispose();
        if (pending is not null) Guard.Run("Mouse: a press count's actions", () => pending.Fire(pending.Count));
    }

    public void CancelAll()
    {
        List<Pending> all;
        lock (_gate)
        {
            all = _pending.Values.ToList();
            _pending.Clear();
        }
        foreach (var pending in all) pending.Timer?.Dispose();
    }

    private void Expired(MouseButton button, ITimer timer)
    {
        Pending? pending;
        lock (_gate)
        {
            if (!_pending.TryGetValue(button, out pending) || pending.Timer != timer) return;
            _pending.Remove(button);
        }
        timer.Dispose();
        Guard.Run("Mouse: a press count's actions", () => pending.Fire(pending.Count));
    }

    public void Dispose() => CancelAll();
}
