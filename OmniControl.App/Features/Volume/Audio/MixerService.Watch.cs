namespace OmniControl.App.Features.Volume.Audio;

/// How the mixer hears about programs between its regular scans: Windows' session event the moment a program opens a new session
/// (<see cref="CoreAudio.SessionWatcher"/>), and, only while an open program has effects to run, a quick look every
/// <see cref="Timing.ProgramSoundPoll"/> at whether those sessions started or stopped playing. Either asks for a scan at once
/// (<see cref="Scan"/>, read on a worker), which raises <see cref="Updated"/>, so the effects engine plans again within milliseconds.
public sealed partial class MixerService
{
    private readonly CoreAudio.SessionWatcher _sessionWatcher = new();
    private readonly DispatcherTimer _soundPoll = new() { Interval = Timing.ProgramSoundPoll };

    /// The mixer's entry in the master list of hardware watches: the playback and recording devices (both lists come from one scan, read on the watch's worker).
    public const string HardwareWatchId = "audio.devices";

    private void StartWatching()
    {
        _hardware.Register(new HardwareWatch(HardwareWatchId, "Sound devices",
            HardwareSignal.Audio | HardwareSignal.Device | HardwareSignal.Session | HardwareSignal.Resume, ScanForWatch));
        _sessionWatcher.DevicesChanged += OnSoundDevicesChanged;
        _sessionWatcher.Created += OnSessionCreated;
        _sessionWatcher.Start();
        _soundPoll.Tick += OnSoundPoll;
    }

    /// The sound poll runs only while an open program has effects that run (what it looks at); otherwise the UI thread is left alone.
    private void SyncSoundPoll()
    {
        if (_disposed) return;
        _soundPoll.IsEnabled = Running.Any(app => app.IsOpen && Find(app.Key) is { } entry && FxSlot.AnyRuns(entry.FxOn, entry.Fx));
    }

    private void StopWatching()
    {
        _soundPoll.Stop();
        _soundPoll.Tick -= OnSoundPoll;
        _sessionWatcher.Created -= OnSessionCreated;
        _sessionWatcher.DevicesChanged -= OnSoundDevicesChanged;
        _sessionWatcher.Dispose();
    }

    /// Worker thread: Windows added, removed, enabled or disabled a sound device; the watch refreshes once the burst is over.
    private void OnSoundDevicesChanged() => _hardware.Signal(HardwareSignal.Audio);

    /// Worker thread: a burst of new sessions (a browser opens several) becomes one scan (<see cref="Scan"/> runs one read at a time).
    private void OnSessionCreated() => Scan();

    /// Scans at once when a program with effects started or stopped playing since the last scan.
    private void OnSoundPoll(object? sender, EventArgs e)
    {
        foreach (var app in Running)
        {
            if (!app.IsOpen || Find(app.Key) is not { } entry || !FxSlot.AnyRuns(entry.FxOn, entry.Fx)) continue;
            if (!_sessions.TryGetValue(app.Key, out var list) || list.Any(s => s.IsPlayingNow) == app.IsPlaying) continue;
            Scan();
            return;
        }
    }
}
