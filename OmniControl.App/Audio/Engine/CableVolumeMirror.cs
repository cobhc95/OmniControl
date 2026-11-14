using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// Keeps the bus cable's volume and mute equal to the real device's while the cable is Windows' default (Full control). The hardware
/// volume keys, Windows' flyout and other programs change the default device, which is the cable, not the speakers the bus plays on;
/// so a change of the cable is copied to the real device, and a change of the real device (OmniControl's own slider, its hotkeys)
/// is copied to the cable. Both sit at the same level, so one key press raises the real device by exactly Windows' step. An echo of
/// a write made here is told by its value and <see cref="Timing.MirrorEchoGrace"/>, so the two never ping-pong. Writes run on a
/// worker; the watchers are Core's <see cref="CoreAudio.MasterWatcher"/>.
/// </summary>
internal sealed class CableVolumeMirror : IDisposable
{
    private readonly MixerService _mixer;
    private readonly object _gate = new();
    private readonly CoreAudio.MasterWatcher _cableWatch = new();
    private readonly CoreAudio.MasterWatcher _realWatch = new();
    private readonly List<(bool OnCable, int Percent, bool Muted, DateTime At)> _writes = new();
    private string? _cable, _real;
    private bool _started, _disposed;

    public CableVolumeMirror(MixerService mixer)
    {
        _mixer = mixer;
        _cableWatch.Device = () => Volatile.Read(ref _cable);
        _realWatch.Device = () => Volatile.Read(ref _real);
        _cableWatch.Changed += (percent, muted) => Task.Run(() => Follow(true, percent, muted));
        _realWatch.Changed += (percent, muted) => Task.Run(() => Follow(false, percent, muted));
    }

    /// <summary>Mirrors <paramref name="cable"/> and <paramref name="real"/> from now on; the cable first takes the real device's level.</summary>
    public void Start(string cable, string real)
    {
        if (_disposed) return;
        var same = Volatile.Read(ref _cable) == cable && Volatile.Read(ref _real) == real;
        Volatile.Write(ref _cable, cable);
        Volatile.Write(ref _real, real);
        if (same) return;
        Task.Run(() => Adopt(cable, real));
        if (!_started)
        {
            _started = true;
            _cableWatch.Start();
            _realWatch.Start();
        }
        else
        {
            _cableWatch.Retarget();
            _realWatch.Retarget();
        }
    }

    /// <summary>Stops mirroring; the cable stays at the real device's level and is unmuted, so nothing is left silent.</summary>
    public void Stop()
    {
        var cable = Interlocked.Exchange(ref _cable, null);
        Volatile.Write(ref _real, null);
        if (cable is null) return;
        Task.Run(() =>
        {
            lock (_gate)
            {
                if (_mixer.ReadLevel(cable, out var percent, out _)) _mixer.WriteLevel(cable, percent, false);
            }
        });
    }

    private void Adopt(string cable, string real)
    {
        lock (_gate)
        {
            if (!_mixer.ReadLevel(real, out var percent, out var muted)) return; // unplugged: the next real-device change tries again
            Write(true, cable, percent, muted);
        }
    }

    private void Follow(bool fromCable, int percent, bool muted)
    {
        lock (_gate)
        {
            if (_disposed || IsEcho(fromCable, percent, muted)) return;
            var cable = Volatile.Read(ref _cable);
            var real = Volatile.Read(ref _real);
            if (cable is null || real is null) return;
            if (fromCable) Write(false, real, percent, muted);
            else Write(true, cable, percent, muted);
        }
    }

    /// <summary>Writes a level to one side and remembers it, so its notification is recognised as ours. Call inside the lock.</summary>
    private void Write(bool onCable, string device, int percent, bool muted)
    {
        _writes.Add((onCable, percent, muted, DateTime.UtcNow));
        _mixer.WriteLevel(device, percent, muted);
    }

    /// <summary>True (and forgotten) when the notification is the echo of a recent write here. Call inside the lock.</summary>
    private bool IsEcho(bool onCable, int percent, bool muted)
    {
        _writes.RemoveAll(w => !Grace.Within(w.At, Timing.MirrorEchoGrace));
        var i = _writes.FindIndex(w => w.OnCable == onCable && w.Percent == percent && w.Muted == muted);
        if (i < 0) return false;
        _writes.RemoveAt(i);
        return true;
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        _cableWatch.Dispose();
        _realWatch.Dispose();
    }
}
