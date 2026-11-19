using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Pages.Easy.Flow;

namespace OmniControl.App.Features.Volume.Pages.Easy;

/// One line of the Easy page: the master ("Everything"), a program or a microphone. It wraps the very same <see cref="VolumeRow"/> the
/// Advanced Mixer shows (name, icon, level, mute, the engine's status and fix), so a change on either page is the other's too, and adds
/// the row's Sound drawer (<see cref="EasySound"/>) and a plain state word.
public sealed partial class EasyRow : ObservableObject, IDisposable
{
    public EasyRow(VolumeRow row, MixerService mixer, EffectsEngine engine, Action<string?> openFx, Func<EasyConfirm, Task<bool>> confirm)
    {
        Row = row;
        Sound = new EasySound(mixer, engine, row.ChainKey, () => openFx(row.ChainKey), WhyNot, confirm);
    }

    public VolumeRow Row { get; }
    public EasySound Sound { get; }

    /// "playing", "open", "not open" for a program; "microphone" for a recording device; empty for the master.
    [ObservableProperty] private string _state = string.Empty;

    /// Whether the program is playing sound now (from the mixer's last look); only a program has it.
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

    /// Why this row's sound cannot be applied now, as one sentence (the drawer greys itself with it); null = it can. The reasons are the
    /// ones the Advanced row already works out: a setup step missing.
    private string? WhyNot()
    {
        if (Row.HasFix) return Row.Status.Length > 0 ? Row.Status : "It needs a setup step first.";
        return null;
    }

    public void Dispose() => Sound.Dispose();
}
