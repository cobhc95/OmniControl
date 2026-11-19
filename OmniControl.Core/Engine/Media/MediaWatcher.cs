using Windows.Media.Control;

namespace OmniControl.Core.Engine.Media;

/// What changed in the media that is playing: a new track (with its title and artist), or play/pause. <paramref name="Program"/> is the
/// program whose session it is (<see cref="ProgramIdentity.Normalize"/>), or empty when Windows names it in a way that is no program file (a Store app).
public sealed record MediaChange(bool IsTrack, bool IsPlaying, string Title, string Artist, string Program);

/// Raises <see cref="Changed"/> (on a worker thread) when any app playing media (browser, music player, video player) starts, pauses
/// or changes track. It listens to Windows' media session list, the same source the volume pop-up's media card uses.
public sealed class MediaWatcher : IDisposable
{
    private sealed class Tracked
    {
        public GlobalSystemMediaTransportControlsSessionPlaybackStatus? Status;
        public string Title = string.Empty, Artist = string.Empty, Program = string.Empty;
    }

    private readonly TimeSpan _trackQuiet;
    private readonly object _gate = new();
    private readonly Dictionary<GlobalSystemMediaTransportControlsSession, Tracked> _sessions = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private DateTime _lastTrackChange = DateTime.MinValue;

    public event Action<MediaChange>? Changed;

    /// <param name="trackQuiet">A play/pause change within this time of a track change is part of the skip and not reported.</param>
    public MediaWatcher(TimeSpan trackQuiet) => _trackQuiet = trackQuiet;

    public void Start() => _ = Task.Run(StartAsync);

    private async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            AttachNewSessions();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Windows' media sessions are unavailable (older Windows, or blocked): there is simply no media pop-up.
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, object? args) => AttachNewSessions();

    /// Starts listening to sessions that appeared, and stops listening to ones that closed.
    private void AttachNewSessions()
    {
        if (_manager is null) return;
        var current = _manager.GetSessions().ToList();

        List<GlobalSystemMediaTransportControlsSession> gone, fresh;
        lock (_gate)
        {
            gone = _sessions.Keys.Where(s => !current.Contains(s)).ToList();
            fresh = current.Where(s => !_sessions.ContainsKey(s)).ToList();
            foreach (var s in gone) _sessions.Remove(s);
            foreach (var s in fresh) _sessions[s] = new Tracked { Status = s.GetPlaybackInfo()?.PlaybackStatus, Program = ProgramOf(s) };
        }

        foreach (var s in gone) Detach(s);
        foreach (var s in fresh)
        {
            s.PlaybackInfoChanged += OnPlaybackInfoChanged;
            s.MediaPropertiesChanged += OnPropertiesChanged;
            _ = ReadPropertiesAsync(s, announce: false); // what is already playing is not news
        }
    }

    private void Detach(GlobalSystemMediaTransportControlsSession session)
    {
        session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        session.MediaPropertiesChanged -= OnPropertiesChanged;
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession session, object? args)
    {
        var status = session.GetPlaybackInfo()?.PlaybackStatus;
        if (status is not (GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)) return;

        MediaChange? change = null;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(session, out var tracked)) return;
            var before = tracked.Status;
            tracked.Status = status;
            var isNews = before is not null && before != status && DateTime.UtcNow - _lastTrackChange > _trackQuiet;
            if (isNews) change = new MediaChange(false, status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, tracked.Title, tracked.Artist, tracked.Program);
        }
        if (change is not null) Changed?.Invoke(change);
    }

    /// The program of a session: Windows names a desktop program's session after its file ("Spotify.exe") or its short name ("Chrome",
    /// "MSEdge"), which is the program's identity; a Store app's name ("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic") is not, so it is unknown.
    private static string ProgramOf(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var id = session.SourceAppUserModelId ?? string.Empty;
            return id.Contains('!') ? string.Empty : ProgramIdentity.Normalize(id);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return string.Empty; // the player closed while it was asked: unknown, so only the event's own setting applies
        }
    }

    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession session, object? args) =>
        _ = ReadPropertiesAsync(session, announce: true);

    private async Task ReadPropertiesAsync(GlobalSystemMediaTransportControlsSession session, bool announce)
    {
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            if (properties is null) return;

            MediaChange? change = null;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(session, out var tracked)) return;
                var isNew = tracked.Title != properties.Title || tracked.Artist != properties.Artist;
                tracked.Title = properties.Title;
                tracked.Artist = properties.Artist;
                if (isNew && announce && properties.Title.Length > 0)
                {
                    _lastTrackChange = DateTime.UtcNow;
                    change = new MediaChange(true, tracked.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, properties.Title, properties.Artist, tracked.Program);
                }
            }
            if (change is not null) Changed?.Invoke(change);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // The player closed while its details were being read: nothing to show.
        }
    }

    public void Dispose()
    {
        if (_manager is not null) _manager.SessionsChanged -= OnSessionsChanged;
        List<GlobalSystemMediaTransportControlsSession> all;
        lock (_gate)
        {
            all = _sessions.Keys.ToList();
            _sessions.Clear();
        }
        foreach (var s in all) Detach(s);
    }
}
