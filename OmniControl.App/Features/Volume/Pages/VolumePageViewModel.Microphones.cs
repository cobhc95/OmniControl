using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// The Microphones group's "Use microphones" switch (<see cref="MixerService.MicrophonesOn"/>): off, the page opens no microphone (no
/// meter capture, no Listen) and the engine runs none; the rows grey what needs one.
public sealed partial class VolumePageViewModel
{
    /// The switch on the Microphones group's header line.
    public ToggleItem UseMicrophones { get; }

    private ToggleItem NewUseMicrophones() =>
        new(EffectsRules.UseMicrophonesLabel, _mixer.MicrophonesOn, _mixer.SetMicrophones) { Tip = EffectsRules.UseMicrophonesTip };

    /// The switch changed (here, by its action, a restore or a preset): off lets go of every meter capture at once; the rows follow.
    private void OnMicrophonesChanged()
    {
        UseMicrophones.Load(_mixer.MicrophonesOn);
        if (!_mixer.MicrophonesOn)
        {
            _captures.Keep(Array.Empty<string>());
            foreach (var row in Inputs.Items.OfType<VolumeRow>()) row.Meter.Clear();
        }
        foreach (var row in Inputs.Items.OfType<VolumeRow>()) SyncRow(row);
    }
}
