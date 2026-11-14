using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>
/// One line of the Easy page: the master ("Everything"), a program or a microphone. It wraps the very same <see cref="VolumeRow"/> the
/// Advanced Mixer shows (name, icon, level, mute, the engine's status and fix), so a change on either page is the other's too, and adds
/// the row's Sound drawer (<see cref="EasySound"/>) and a plain state word.
/// </summary>
public sealed partial class EasyRow : ObservableObject, IDisposable
{
    public EasyRow(VolumeRow row, MixerService mixer, EffectsEngine engine, Action<string?> openFx)
    {
        Row = row;
        Sound = new EasySound(mixer, engine, row.ChainKey, () => openFx(row.ChainKey), WhyNot);
    }

    public VolumeRow Row { get; }
    public EasySound Sound { get; }

    /// <summary>"playing", "open", "not open" for a program; "microphone" for a recording device; empty for the master.</summary>
    [ObservableProperty] private string _state = string.Empty;

    /// <summary>Whether the program is playing sound now (from the mixer's last look); only a program has it.</summary>
    public void Update(bool playing)
    {
        State = Row switch
        {
            MixerItem { IsMaster: false, IsOpen: true } => playing ? "playing" : "open",
            MixerItem { IsMaster: false } => "not open",
            InputItem => "microphone",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Why this row's sound cannot be applied now, as one sentence (the drawer greys itself with it); null = it can. The reasons are the
    /// ones the Advanced row already works out: a mode that cannot run the effects, a program that cannot be recorded, a setup step missing.
    /// </summary>
    private string? WhyNot()
    {
        if (Row.EffectsGate.Reason is { } gate) return gate;
        if (Row is MixerItem { IsMaster: false } && Row.CaptureGate.Reason is { } record) return record;
        if (Row.HasFix) return Row.Status.Length > 0 ? Row.Status : "It needs a setup step first.";
        return null;
    }

    public void Dispose() => Sound.Dispose();
}
