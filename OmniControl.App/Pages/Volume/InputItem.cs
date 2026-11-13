using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// A Microphones row: one recording device (its Windows level and mute; its effects run in Equalizer APO on that microphone in Fast
/// mode, through its own cable in Full control).
/// </summary>
public sealed class InputItem : VolumeRow
{
    /// <summary>The microphone outline shared by microphone rows.</summary>
    internal const string MicGlyph = "M12,3 A3,3 0 0 1 15,6 V12 A3,3 0 0 1 9,12 V6 A3,3 0 0 1 12,3 Z M6,11 A6,6 0 0 0 18,11 M12,17 V21";

    public InputItem(string id, Func<ModeGate> newGate) : base(MixerService.InputPrefix + id, newGate) => Id = id;

    /// <summary>The device's endpoint id.</summary>
    public string Id { get; }

    public override string? Glyph => MicGlyph;
    public override string VolumeTip => "Input level";
    protected override bool SwitchNeedsEffects => true;
}

/// <summary>
/// A combined device's row (Full control): combined speakers in Outputs ("→" its members; no level of its own, no effects),
/// a combined microphone in Microphones ("←" its members, with its own level, mute and effects).
/// </summary>
public sealed class ComboItem : VolumeRow
{
    private const string SpeakersGlyph = "M3,9 H7 L12,4 V20 L7,15 H3 Z M16,8 V16 M20,6 V18";

    public ComboItem(CombinedDevice device, Func<ModeGate> newGate) : base(device.Key, newGate)
    {
        IsMicrophone = device.Kind == CombinedKind.Microphones;
        CanForget = true;
    }

    public bool IsMicrophone { get; }
    public override string? Glyph => IsMicrophone ? InputItem.MicGlyph : SpeakersGlyph;
    public override string RouteArrow => IsMicrophone ? RecordsFromArrow : PlaysOnArrow;
    public override bool HasVolume => IsMicrophone;
    public override bool HasEffects => IsMicrophone;
}
