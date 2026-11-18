namespace OmniControl.Core.Engine;

/// <summary>
/// Drops the second telling of one announcement: the hidden window and .NET's <c>SystemEvents</c> both report a display change or a session
/// that came back, so the same kind within <see cref="CoreTiming.HardwareSignalDuplicateMs"/> passes once. Thread-safe (both listeners call it).
/// </summary>
internal sealed class HardwareSignalFilter
{
    private readonly Dictionary<HardwareSignal, long> _last = new();
    private readonly object _gate = new();

    /// <summary>True when <paramref name="signal"/> is news at <paramref name="nowMs"/> (a monotonic clock in milliseconds).</summary>
    public bool Pass(HardwareSignal signal, long nowMs)
    {
        lock (_gate)
        {
            if (_last.TryGetValue(signal, out var seen) && nowMs - seen < CoreTiming.HardwareSignalDuplicateMs) return false;
            _last[signal] = nowMs;
            return true;
        }
    }
}
