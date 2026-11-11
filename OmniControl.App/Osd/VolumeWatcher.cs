using OmniControl.Core.Engine;

namespace OmniControl.App.Osd;

/// <summary>Raises <see cref="Changed"/> (percent, muted; on a worker thread) when the system volume or mute state changes.</summary>
public sealed class VolumeWatcher : IDisposable
{
    private readonly SystemVolumeController _volume = new();
    private Timer? _timer;
    private int _last = -1;
    private bool _lastMuted;

    public event Action<int, bool>? Changed;

    public void Start() => _timer = new Timer(_ => Poll(), null, 0, 250);

    private void Poll()
    {
        try
        {
            if (!_volume.GetVolume(out var percent).Success || !_volume.GetMute(out var muted).Success) return;
            var first = _last < 0;
            var changed = percent != _last || muted != _lastMuted;
            _last = percent;
            _lastMuted = muted;
            if (changed && !first) Changed?.Invoke(percent, muted);
        }
        catch { /* no audio device right now */ }
    }

    public void Dispose() => _timer?.Dispose();
}
