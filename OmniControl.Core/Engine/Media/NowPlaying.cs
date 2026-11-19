using Windows.Media.Control;

namespace OmniControl.Core.Engine.Media;

/// What the current media session of Windows is doing: the app's name, the track's title and artist (empty when the app gives none) and whether it plays.
public sealed record NowPlayingInfo(string App, string Title, string Artist, bool IsPlaying);

/// Windows' current media session (the one the volume pop-up's media card shows): what it plays, and play/pause, next and previous on it.
/// <see cref="Changed"/> is raised on a worker thread when the session, its track or its play state changes.
public sealed class NowPlaying : IDisposable
{
    private readonly object _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private volatile NowPlayingInfo? _current;

    public event Action? Changed;

    /// What plays now, or null when no app has a media session.
    public NowPlayingInfo? Current => _current;

    public void Start() => _ = Task.Run(StartAsync);

    public Task PlayPauseAsync() => Send(s => s.TryTogglePlayPauseAsync());

    public Task NextAsync() => Send(s => s.TrySkipNextAsync());

    public Task PreviousAsync() => Send(s => s.TrySkipPreviousAsync());

    private async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnSessionChanged;
            Attach();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Windows' media sessions are unavailable (older Windows, or blocked): nothing is ever playing.
        }
    }

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object? args) => Attach();

    private void Attach()
    {
        lock (_gate)
        {
            if (_session is not null)
            {
                _session.PlaybackInfoChanged -= OnChanged;
                _session.MediaPropertiesChanged -= OnChanged;
            }
            _session = _manager?.GetCurrentSession();
            if (_session is not null)
            {
                _session.PlaybackInfoChanged += OnChanged;
                _session.MediaPropertiesChanged += OnChanged;
            }
        }
        _ = ReadAsync();
    }

    private void OnChanged(GlobalSystemMediaTransportControlsSession session, object? args) => _ = ReadAsync();

    private async Task ReadAsync()
    {
        try
        {
            GlobalSystemMediaTransportControlsSession? session;
            lock (_gate) session = _session;
            NowPlayingInfo? info = null;
            if (session is not null)
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                var playing = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                info = new NowPlayingInfo(AppName(session.SourceAppUserModelId), properties?.Title ?? string.Empty, properties?.Artist ?? string.Empty, playing);
            }
            if (Equals(info, _current)) return;
            _current = info;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // The player closed while it was being read: the next session change says what is left.
        }
    }

    private async Task Send(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate) session = _session;
        if (session is null) return;
        try
        {
            await command(session);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // The player closed before the command arrived: there is nothing to control any more.
        }
    }

    /// A player's name from its app id: "Spotify.exe" is Spotify, "Microsoft.ZuneMusic_8wekyb3d8bbwe!App" is ZuneMusic.
    internal static string AppName(string appId)
    {
        var name = appId.Split((char)92).Last();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Contains('!')) name = name[..name.IndexOf('!')];
        if (name.Contains('_')) name = name[..name.IndexOf('_')];
        if (name.Contains('.')) name = name[(name.LastIndexOf('.') + 1)..];
        return name.Length == 0 ? "Media" : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_manager is not null) _manager.CurrentSessionChanged -= OnSessionChanged;
            if (_session is not null)
            {
                _session.PlaybackInfoChanged -= OnChanged;
                _session.MediaPropertiesChanged -= OnChanged;
                _session = null;
            }
        }
    }
}
