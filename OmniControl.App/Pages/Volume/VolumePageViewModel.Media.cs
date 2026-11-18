using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.App.States;
using OmniControl.Core.Engine.Media;

namespace OmniControl.App.Pages.Volume;

// What plays (the app, the title and artist) as a state and play/pause, next and previous as actions, from Windows' current media
// session (Core/Engine/Media/NowPlaying). They sit with the Volume page because the sound is what they control.
public sealed partial class VolumePageViewModel
{
    private const string NowPlayingId = NowPlayingText.StateId, MediaWords = "Windows' current media session, reported as it changes.";
        private NowPlaying? _nowPlaying;
    private HotkeyRegistry? _mediaHotkeys;

    private void RegisterMedia(HotkeyRegistry hotkeys)
    {
        var playing = _nowPlaying = new NowPlaying();
        _mediaHotkeys = hotkeys;
        _states.Register(new StateEntry(NowPlayingId, "Now playing", Title, StateKind.Choice, () => NowPlayingText.Of(playing.Current), MediaWords,
            Keywords: "media music song track playing spotify", Short: "Now playing", SuggestedIcons: new[] { "music", "player-play" }, Common: CommonGroups.Sound));
        hotkeys.Register(new[]
        {
            Media(NowPlayingText.PreviousId, "Previous track", "previous back song", playing.PreviousAsync, new[] { "player-track-prev", "prev" }),
            Media(NowPlayingText.PlayPauseId, "Play or pause media", "play pause toggle", playing.PlayPauseAsync, new[] { "player-play", "player-pause" }),
            Media(NowPlayingText.NextId, "Next track", "next skip forward song", playing.NextAsync, new[] { "player-track-next", "next" }),
        });
        playing.Changed += OnNowPlayingChanged;
        playing.Start();
    }

    private HotkeyAction Media(string id, string title, string words, Func<Task> run, string[] icons) =>
        new(id, title, Title, "media music player " + words, ids => { _ = Guard.RunAsync(title, run); }) { SuggestedIcons = icons, Common = CommonGroups.Sound };

    private void OnNowPlayingChanged() => Dispatcher.UIThread.Post(() => _states.Notify(NowPlayingId));

    private void ForgetMedia()
    {
        if (_nowPlaying is null) return;
        _nowPlaying.Changed -= OnNowPlayingChanged;
        _nowPlaying.Dispose();
        foreach (var id in NowPlayingText.ActionIds) _mediaHotkeys?.Unregister(id);
        _states.Unregister(new[] { NowPlayingId });
    }
}
