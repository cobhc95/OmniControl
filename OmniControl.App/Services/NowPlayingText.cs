using OmniControl.App.States;
using OmniControl.Core.Engine.Media;

namespace OmniControl.App.Services;

/// <summary>
/// THE words of the "Now playing" state, made once by the PC that plays and read by any page that shows it (this PC's lists, another PC's
/// card): the text is "Title ‖ Artist · App" (the title alone when nothing else is known), the number is 1 while playing and 0 while paused,
/// and no number with the words "Nothing playing" when no app has a media session. Plain printable characters, because they travel to other PCs.
/// </summary>
public static class NowPlayingText
{
    public const string StateId = "media.nowplaying", PreviousId = "media.previous", PlayPauseId = "media.playpause", NextId = "media.next", NothingWords = "Nothing playing";

    /// <summary>The media actions in the order the card's pills show them.</summary>
    public static IReadOnlyList<string> ActionIds { get; } = new[] { PreviousId, PlayPauseId, NextId };

    private const string Divider = " ‖ ", Join = " · ";

    public static StateValue Of(NowPlayingInfo? info)
    {
        if (info is null) return new StateValue(null, NothingWords, DateTime.Now);
        var title = info.Title.Length > 0 ? info.Title : info.App;
        var detail = string.Join(Join, new[] { info.Artist, info.Title.Length > 0 ? info.App : string.Empty }.Where(p => p.Length > 0));
        return new StateValue(info.IsPlaying ? 1 : 0, detail.Length > 0 ? title + Divider + detail : title, DateTime.Now);
    }

    /// <summary>The middle pill shows what a click will do: Pause while playing, Play otherwise (name and icon id of the icon library).</summary>
    public static (string Name, string Icon) PlayPausePill(bool playing) => playing ? ("Pause", "player-pause") : ("Play", "player-play");

    /// <summary>The small tag at the right of the title line: "Playing" (accent), "Paused" (plain), none when nothing plays.</summary>
    public static IReadOnlyList<TagItem> StateTags(bool playing, bool nothing) =>
        nothing ? Array.Empty<TagItem>() : new[] { playing ? new TagItem("Playing", TagTone.Selected) : new TagItem("Paused") };

    /// <summary>The title, the muted "artist · app" line and whether it plays; the title is empty when nothing plays.</summary>
    public static (string Title, string Detail, bool Playing, bool Nothing) Read(StateValue? value)
    {
        if (value?.Number is null || string.IsNullOrEmpty(value.Text)) return (string.Empty, string.Empty, false, true);
        var at = value.Text.IndexOf(Divider, StringComparison.Ordinal);
        return at < 0 ? (value.Text, string.Empty, value.Number != 0, false)
            : (value.Text[..at], value.Text[(at + Divider.Length)..], value.Number != 0, false);
    }
}
