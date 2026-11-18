using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// The mixer's scan, read off the UI thread: every <see cref="Timing.MixerRefresh"/>, a session event, a sound poll or a hardware
/// signal asks for one, a worker reads the devices and the programs' sessions from Windows (<see cref="Read"/>; a stuck audio service
/// then holds a worker, never the window), and only the result is posted to the UI thread (<see cref="Apply"/>). One read runs at a
/// time; asks while it runs become one more read after it.
/// </summary>
public sealed partial class MixerService
{
    private readonly object _scanGate = new();
    private bool _scanRunning, _scanAgain, _disposed;
    private long _ticket, _appliedTicket;

    /// <summary>The sessions the UI's session objects were made from (<see cref="Regroup"/>), sorted.</summary>
    private IReadOnlyList<SessionKey> _heldSessions = Array.Empty<SessionKey>();

    /// <summary>What one read found: the device lists, every program session (as plain facts) and the processes' program files.</summary>
    /// <param name="Ticket">The read's place in order: an older read that ends after a newer one is dropped.</param>
    private sealed record ScanRead(long Ticket, IReadOnlyList<AudioDevice> Devices, IReadOnlyList<AudioDevice> RecordingDevices,
        IReadOnlyList<SessionKey> Sessions, IReadOnlyDictionary<int, string?> Paths);

    /// <summary>One session as scans compare them: while none changes, the UI keeps the session objects it has.</summary>
    private readonly record struct SessionKey(int ProcessId, string DeviceId, int State)
    {
        public static SessionKey Of(AudioSession s) => new(s.ProcessId, s.DeviceId, s.State);

        public static IReadOnlyList<SessionKey> Sorted(IEnumerable<SessionKey> keys) =>
            keys.OrderBy(k => k.ProcessId).ThenBy(k => k.DeviceId, StringComparer.Ordinal).ThenBy(k => k.State).ToList();
    }

    /// <summary>Asks for a scan (any thread): read on a worker, applied on the UI thread, <see cref="Updated"/> raised there.</summary>
    public void Scan()
    {
        lock (_scanGate)
        {
            if (_scanRunning)
            {
                _scanAgain = true;
                return;
            }
            _scanRunning = true;
        }
        _ = Task.Run(ScanOnWorker);
    }

    /// <summary>Worker: one read, its result posted to the UI thread, then the read asked for meanwhile (if any).</summary>
    private void ScanOnWorker()
    {
        var read = ReadGuarded();
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
            if (again) _ = Task.Run(ScanOnWorker);
        });
    }

    /// <summary>A scan made here and now (the first look at start-up, before the pages exist).</summary>
    private void ScanNow()
    {
        if (ReadGuarded() is { } read) Apply(read);
    }

    /// <summary>The hardware watch's refresh (its own worker): reads there, applies on the UI thread and returns when that is done.</summary>
    private void ScanForWatch()
    {
        if (ReadGuarded() is { } read) Dispatcher.UIThread.Invoke(() => { if (!_disposed) Apply(read); });
    }

    private ScanRead? ReadGuarded()
    {
        ScanRead? read = null;
        Guard.Run("Reading sound devices and programs", () => read = Read());
        return read;
    }

    /// <summary>A worker (the UI thread only for the first look): what Windows has now. Session objects made here are only read, then dropped.</summary>
    private ScanRead Read()
    {
        var ticket = Interlocked.Increment(ref _ticket);
        var devices = CoreAudio.Devices();
        var recording = CoreAudio.InputDevices();
        var sessions = SessionKey.Sorted(CoreAudio.Sessions().Where(s => s.ProcessId != Environment.ProcessId).Select(SessionKey.Of));
        var paths = sessions.Select(s => s.ProcessId).Distinct().ToDictionary(pid => pid, CoreAudio.ProcessPath);
        return new ScanRead(ticket, devices, recording, sessions, paths);
    }

    private bool SameSessions(IReadOnlyList<SessionKey> read) => read.SequenceEqual(_heldSessions);
}
