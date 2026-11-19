using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Features.OtherPcs;

/// THE wake status of a PC: the left half of the split Wake button says it, on a PC's header and on this PC's title line, with the same words and dot colours (tone Good = green, Caution = yellow, Neutral = grey). Pure.
public static class WakeStatusWords
{
    public static TagItem Of(WakeAdvice advice, WakeFacts? facts) => advice.State switch
    {
        WakeState.CanWake => new TagItem("Wake ready", TagTone.Good, $"It can be woken over the network ({facts?.Adapter})."),
        WakeState.SettingOff => new TagItem("Wake not set up", TagTone.Caution, $"{advice.Reason} Its network card can wake it, but a setting is off. The cogwheel opens the set-up."),
        WakeState.Unconfirmed => new TagItem("Wake unconfirmed", TagTone.Neutral, advice.Reason),
        WakeState.Cannot => new TagItem("Can't wake", TagTone.Neutral, advice.Reason),
        _ => new TagItem("Wake unknown", TagTone.Neutral, "It has not told this PC how it can be woken yet."),
    };
}
