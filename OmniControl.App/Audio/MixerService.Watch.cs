using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// How the mixer hears about programs between its regular scans (<see cref="Timing.MixerRefresh"/>): Windows' session event the moment
/// a program opens a new session (<see cref="CoreAudio.SessionWatcher"/>), and a quick look every <see cref="Timing.ProgramSoundPoll"/>
/// at whether the sessions of programs with effects started or stopped playing. Either starts a scan at once, which raises
/// <see cref="Updated"/>, so the effects engine plans again within milliseconds instead of up to two seconds later.
/// </summary>
public sealed partial class MixerService
{
    private readonly CoreAudio.SessionWatcher _sessionWatcher = new();
    private readonly DispatcherTimer _soundPoll = new() { Interval = Timing.ProgramSoundPoll };
    private readonly object _postGate = new();
    private bool _scanPosted;

    private void StartWatching()
    {
        _sessionWatcher.Created += OnSessionCreated;
        _sessionWatcher.Start();
        _soundPoll.Tick += OnSoundPoll;
        _soundPoll.Start();
    }

    private void StopWatching()
    {
        _soundPoll.Stop();
        _soundPoll.Tick -= OnSoundPoll;
        _sessionWatcher.Created -= OnSessionCreated;
        _sessionWatcher.Dispose();
    }

    /// <summary>Worker thread: a burst of new sessions (a browser opens several) becomes one scan on the UI thread.</summary>
    private void OnSessionCreated()
    {
        lock (_postGate)
        {
            if (_scanPosted) return;
            _scanPosted = true;
        }
        Dispatcher.UIThread.Post(() =>
        {
            lock (_postGate) _scanPosted = false;
            Scan();
        });
    }

    /// <summary>Scans at once when a program with effects started or stopped playing since the last scan.</summary>
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
