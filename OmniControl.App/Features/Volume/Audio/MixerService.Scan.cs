namespace OmniControl.App.Features.Volume.Audio;

/// The mixer's scan, read off the UI thread: the regular timer (<see cref="Timing.MixerRefresh"/> while a view that shows the mixer is
/// on screen, <see cref="Timing.MixerRefreshIdle"/> otherwise), a session event, a sound poll or a hardware signal asks for one, a worker
/// reads the programs' sessions from Windows (<see cref="Read"/>; a stuck audio service then holds a worker, never the window), and only
/// the result is posted to the UI thread (<see cref="Apply"/>). The device lists are read only on a sound-device signal (the hardware
/// watch), on <see cref="ScanAll"/> and until the first list arrived. One read runs at a time; asks while it runs become one more read after it.
/// <see cref="Updated"/> is raised only when the result differs from what the mixer held, while a view watches closely
/// (<see cref="WatchClosely"/>: values Windows does not announce, such as a non-default microphone's level, stay fresh there), and after
/// <see cref="ScanAll"/>.
public sealed partial class MixerService
{
    private readonly object _scanGate = new();
    private bool _scanRunning, _scanAgain, _wantDevices, _wantAnnounce, _disposed;
    private volatile bool _devicesKnown;
    private long _ticket, _appliedTicket, _devicesTicket;

    /// The sessions the UI's session objects were made from (<see cref="Regroup"/>), sorted.
    private IReadOnlyList<SessionKey> _heldSessions = Array.Empty<SessionKey>();

    /// What one read found: the device lists (null: not read this time, the held ones stay), every program session (as plain facts) and
    /// the processes' program files.
    /// <param name="Ticket">The read's place in order: an older read that ends after a newer one is dropped.</param>
    /// <param name="Announce">Raise <see cref="Updated"/> even when nothing differs.</param>
    private sealed record ScanRead(long Ticket, IReadOnlyList<AudioDevice>? Devices, IReadOnlyList<AudioDevice>? RecordingDevices,
        IReadOnlyList<SessionKey> Sessions, IReadOnlyDictionary<int, string?> Paths, bool Announce);

    /// One session as scans compare them: while none changes, the UI keeps the session objects it has.
    private readonly record struct SessionKey(int ProcessId, string DeviceId, int State)
    {
        public static SessionKey Of(AudioSession s) => new(s.ProcessId, s.DeviceId, s.State);

        public static SessionKey Of(SessionFact s) => new(s.ProcessId, s.DeviceId, s.State);

        public static IReadOnlyList<SessionKey> Sorted(IEnumerable<SessionKey> keys) =>
            keys.OrderBy(k => k.ProcessId).ThenBy(k => k.DeviceId, StringComparer.Ordinal).ThenBy(k => k.State).ToList();
    }

    /// Asks for a scan of the programs (any thread): read on a worker, applied on the UI thread, <see cref="Updated"/> raised there when it differs.
    public void Scan() => Request(devices: false, announce: false);

    /// Asks for a full scan (the devices too) whose result is announced even when nothing changed: the Volume page opened, a program
    /// added or forgotten, the default device changed, settings read again.
    public void ScanAll() => Request(devices: true, announce: true);

    private void Request(bool devices, bool announce)
    {
        lock (_scanGate)
        {
            _wantDevices |= devices;
            _wantAnnounce |= announce;
            if (_scanRunning)
            {
                _scanAgain = true;
                return;
            }
            _scanRunning = true;
        }
        Guard.OffUi("Mixer: scan", ScanOnWorker);
    }

    /// Worker: one read, its result posted to the UI thread, then the read asked for meanwhile (if any).
    private void ScanOnWorker()
    {
        bool devices, announce;
        lock (_scanGate)
        {
            (devices, announce) = (_wantDevices || !_devicesKnown, _wantAnnounce);
            _wantDevices = _wantAnnounce = false;
        }
        var read = ReadGuarded(devices, announce);
        Dispatcher.UIThread.Post(() =>
        {
            if (read is not null && !_disposed) Guard.Run("Mixer scan", () => Apply(read));
            bool again;
            lock (_scanGate)
            {
                again = _scanAgain && !_disposed;
                _scanAgain = false;
                _scanRunning = again;
            }
            if (again) Guard.OffUi("Mixer: scan", ScanOnWorker);
        });
    }

    /// The hardware watch's refresh (its own worker): reads the devices and programs there, applies on the UI thread and returns when that is done.
    private void ScanForWatch()
    {
        if (ReadGuarded(devices: true, announce: false) is { } read) Dispatcher.UIThread.Invoke(() => { if (!_disposed) Apply(read); });
    }

    private ScanRead? ReadGuarded(bool devices, bool announce)
    {
        ScanRead? read = null;
        Guard.Run("Reading sound devices and programs", () => read = Read(devices, announce));
        return read;
    }

    /// A worker: what Windows has now, as plain facts (every COM object of the read is given back at once).
    private ScanRead Read(bool devices, bool announce)
    {
        var ticket = Interlocked.Increment(ref _ticket);
        var playback = devices ? CoreAudio.Devices() : null;
        var recording = devices ? CoreAudio.InputDevices() : null;
        var sessions = SessionKey.Sorted(CoreAudio.SessionFacts().Where(s => s.ProcessId != Environment.ProcessId).Select(SessionKey.Of));
        var paths = sessions.Select(s => s.ProcessId).Distinct().ToDictionary(pid => pid, pid => _knownPaths.GetOrAdd(pid, CoreAudio.ProcessPath));
        foreach (var gone in _knownPaths.Keys.Where(pid => !paths.ContainsKey(pid)).ToList()) _knownPaths.TryRemove(gone, out _); // a process with no session any more
        return new ScanRead(ticket, playback, recording, sessions, paths, announce);
    }

    // Each program's path is read once while it has a session (a process id is not reused while its process lives); workers only.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string?> _knownPaths = new();

    private bool SameSessions(IReadOnlyList<SessionKey> read) => read.SequenceEqual(_heldSessions);
}
