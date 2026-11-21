namespace OmniControl.App.Features.Input.Mouse;

/// <c>trackpad-log.txt</c>: what the touchpad watcher and the takeover of Windows' own gestures did (every write with the value read back),
/// plus one line per recognised gesture with Windows' matching setting read at that moment, so a gesture Windows still acted on can be
/// matched to what its setting said then. Gesture lines are limited to <see cref="Timing.TrackpadLogGestureLimit"/> per
/// <see cref="Timing.TrackpadLogGestureWindow"/>; every other line is always written. Safe from any thread.
public sealed class TrackpadLog
{
    private const string FileName = LogFiles.Trackpad;

    private readonly object _gate = new();
    private readonly Queue<DateTime> _gestureLines = new();

    /// Writes a line (watcher and takeover events).
    public void Write(string line)
    {
        lock (_gate) AppDataStore.AppendLog(FileName, line);
    }

    /// True when a gesture line may be written now (and counts it); false once this minute's lines are used up.
    public bool TakeGestureLine()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            while (_gestureLines.Count > 0 && now - _gestureLines.Peek() > Timing.TrackpadLogGestureWindow) _gestureLines.Dequeue();
            if (_gestureLines.Count >= Timing.TrackpadLogGestureLimit) return false;
            _gestureLines.Enqueue(now);
            return true;
        }
    }
}
