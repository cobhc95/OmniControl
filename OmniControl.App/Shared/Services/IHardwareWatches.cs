namespace OmniControl.App.Shared.Services;

/// What a feature that depends on hardware needs of the master list of hardware watches (<see cref="HardwareWatches"/> is the one).
public interface IHardwareWatches
{
    /// Every power and session event (sleep, wake, lock, display off and on...), raised on a listener's thread.
    event Action<PowerSignal>? Power;

    /// What Windows last said about the console's screens as a whole.
    ScreenLight Screens { get; }

    /// The screens went off, came on or dimmed (on a listener's thread; handlers must return at once).
    event Action? ScreensChanged;

    /// Adds a watch; the same id twice replaces the first.
    void Register(HardwareWatch watch);

    /// A Windows announcement arrived (any thread): every watch that lists it is refreshed once its burst has settled.
    void Signal(HardwareSignal signal, string? source = null);

    /// A watch's own feature noticed a change: that watch alone is refreshed after the settle time.
    void Request(string id);

    /// Refreshes the watch now (worker thread), then calls <paramref name="then"/>; false, with nothing done, when there is no such watch or it was refreshed for an action a moment ago.
    bool RefreshThen(string id, Action then);
}
