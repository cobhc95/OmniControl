using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// A Microphones row: one recording device (its Windows level and mute; its effects run through its own cable).
/// </summary>
public sealed class InputItem : VolumeRow
{
    /// <summary>The microphone outline shared by microphone rows.</summary>
    internal const string MicGlyph = "M12,3 A3,3 0 0 1 15,6 V12 A3,3 0 0 1 9,12 V6 A3,3 0 0 1 12,3 Z M6,11 A6,6 0 0 0 18,11 M12,17 V21";

    public InputItem(string id) : base(MixerService.InputPrefix + id) => Id = id;

    /// <summary>The device's endpoint id.</summary>
    public string Id { get; }

    public override string? Glyph => MicGlyph;
    public override string VolumeTip => "Input level";
}

/// <summary>
/// A combined device's row: combined speakers in Outputs (plays on its members; no level of its own, no effects), a
/// combined microphone in Microphones (records from its members, with its own level, mute and effects).
/// </summary>
public sealed class ComboItem : VolumeRow
{
    /// <summary>The one word for a combined device in a tag (the Wiring Source column, the menus' entries).</summary>
    public const string CombinedTag = "Combined";

    private const string SpeakersGlyph = "M3,9 H7 L12,4 V20 L7,15 H3 Z M16,8 V16 M20,6 V18";

    public ComboItem(CombinedDevice device) : base(device.Key)
    {
        IsMicrophone = device.Kind == CombinedKind.Microphones;
        CanForget = true;
    }

    public bool IsMicrophone { get; }
    public override string? Glyph => IsMicrophone ? InputItem.MicGlyph : SpeakersGlyph;
    public override bool HasVolume => IsMicrophone;
    public override bool HasEffects => IsMicrophone;

    /// <summary>A combined device sits among the real ones in its Wiring table, so it says what it is.</summary>
    public override string? SourceTag => CombinedTag;
}
