namespace OmniControl.App.Services;

/// <summary>
/// The one rate limit on starting programs and folders on its own (AGENTS.md safety principle): at most <see cref="Timing.LaunchBurstMax"/>
/// starts per <see cref="Timing.LaunchBurstWindow"/>, so a repeating or looping mapping can never flood the desktop with windows.
/// </summary>
public sealed class LaunchLimiter
{
    private readonly Queue<DateTime> _launches = new();
    private readonly int _max;
    private readonly TimeSpan _window;

    /// <param name="max">How many are allowed per <paramref name="window"/> (the launch limit when not given).</param>
    public LaunchLimiter(int? max = null, TimeSpan? window = null)
    {
        _max = max ?? Timing.LaunchBurstMax;
        _window = window ?? Timing.LaunchBurstWindow;
    }

    /// <summary>True (and the start is counted) when another start is allowed now.</summary>
    public bool TryAcquire()
    {
        lock (_launches)
        {
            var now = DateTime.UtcNow;
            while (_launches.Count > 0 && now - _launches.Peek() > _window) _launches.Dequeue();
            if (_launches.Count >= _max) return false;
            _launches.Enqueue(now);
            return true;
        }
    }
}
