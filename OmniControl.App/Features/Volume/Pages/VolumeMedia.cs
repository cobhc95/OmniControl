using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;
using OmniControl.Core.Engine.Media;

namespace OmniControl.App.Features.Volume.Pages;

/// What plays (the app, the title and artist) as a state and play/pause, next and previous as actions, from Windows' current media session
/// (<see cref="NowPlaying"/>). They sit with the Volume page because the sound is what they control.
internal sealed class VolumeMedia : IDisposable
{
    private const string NowPlayingId = NowPlayingText.StateId, MediaWords = "Windows' current media session, reported as it changes.";

    private readonly NowPlaying _playing = new();
    private readonly IActionRegistry _hotkeys;
    private readonly IStateRegistry _states;
    private readonly string _title;

    /// Registers the state and the actions under the page's <paramref name="title"/> and starts following the media session.
    public VolumeMedia(IActionRegistry hotkeys, IStateRegistry states, string title)
    {
        _hotkeys = hotkeys;
        _states = states;
        _title = title;
        var playing = _playing;
        states.Register(new StateEntry(NowPlayingId, "Now playing", title, StateKind.Choice, () => NowPlayingText.Of(playing.Current), MediaWords,
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
        new(id, title, _title, "media music player " + words, ids => { _ = Guard.RunAsync(title, run); }) { SuggestedIcons = icons, Common = CommonGroups.Sound };

    private void OnNowPlayingChanged() => Dispatcher.UIThread.Post(() => _states.Notify(NowPlayingId));

    public void Dispose()
    {
        _playing.Changed -= OnNowPlayingChanged;
        _playing.Dispose();
        foreach (var id in NowPlayingText.ActionIds) _hotkeys.Unregister(id);
        _states.Unregister(new[] { NowPlayingId });
    }
}
