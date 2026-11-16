using System.ComponentModel;
using OmniControl.App.States;

namespace OmniControl.App.Pages.Volume;

// The page's states in the master list of states: the master's volume, mute and effects (what its row shows), Windows' default playback
// device and the default microphone's mute. The master row is kept in step with Windows by every scan, so its values are what is read;
// a change of any of them notifies at once. Each can be set from outside the page (the tray flyout) the way the page's own control does.
public sealed partial class VolumePageViewModel
{
    private const string MasterVolumeId = "volume.master.level", MasterMutedId = "volume.master.muted", MasterFxId = "volume.master.fx",
        DefaultSpeakerId = "volume.default.speaker", MicMutedId = "volume.mic.muted";
    private static readonly string[] StateIds = { MasterVolumeId, MasterMutedId, MasterFxId, DefaultSpeakerId, MicMutedId };

    private const string ScanWords = "Windows' own value, looked at every few seconds and whenever it is changed here.";
    private const string FxWords = "What the master row's FX switch shows.";

    private void RegisterStates()
    {
        _states.Register(new[]
        {
            new StateEntry(MasterVolumeId, "Master volume", Title, StateKind.Level, () => StateValue.Of(_master.Volume, DateTime.Now), ScanWords,
                Unit: "%", Keywords: "sound level loud quiet", Set: value => { if (value.Number is { } level) _master.Volume = (int)Math.Round(level); },
                Short: "Volume"),
            new StateEntry(MasterMutedId, "Master muted", Title, StateKind.OnOff, () => StateValue.Of(_master.Muted, DateTime.Now), ScanWords,
                Keywords: "sound mute silent", Set: value => _master.Muted = IsOn(value), Short: "Mute"),
            new StateEntry(MasterFxId, "Master effects on", Title, StateKind.OnOff, () => StateValue.Of(_master.FxOn, DateTime.Now), FxWords,
                Keywords: "sound fx effects chain equaliser", Set: value => _mixer.SetFxByUser(null, IsOn(value)), Short: "FX"),
            new StateEntry(DefaultSpeakerId, "Default speaker", Title, StateKind.Choice,
                () => _mixer.Devices.FirstOrDefault(d => d.IsDefault) is { } device ? StateValue.Of(device.Name, DateTime.Now) : null, ScanWords,
                Choices: () => _mixer.Devices.Select(d => d.Name).Distinct().ToList(), Keywords: "sound output playback device headphones",
                Set: value => { if (_mixer.Devices.FirstOrDefault(d => d.Name == value.Text) is { } device) MakeMasterDevice(device.Id); }),
            new StateEntry(MicMutedId, "Microphone muted", Title, StateKind.OnOff,
                () => DefaultInput() is { } input ? StateValue.Of(input.Muted, DateTime.Now) : null, ScanWords,
                Keywords: "sound audio input microphone mic mute silence", Short: "Mic mute",
                Set: value => { if (DefaultInput() is { } input && input.Muted != IsOn(value)) StepInput(0, toggleMute: true); }),
        });
        _master.PropertyChanged += OnMasterValue;
        _mixer.Updated += NotifyScanned;
    }

    private static bool IsOn(StateValue value) => value.Number is { } number && number != 0;

    /// <summary>The default recording device's row, or null when there is none.</summary>
    private InputItem? DefaultInput() =>
        _mixer.RecordingDevices.FirstOrDefault(d => d.IsDefault) is { } device && _inputs.TryGetValue(device.Id, out var input) ? input : null;

    private void OnMasterValue(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VolumeRow.Volume)) _states.Notify(MasterVolumeId);
        else if (e.PropertyName == nameof(VolumeRow.Muted)) _states.Notify(MasterMutedId);
        else if (e.PropertyName == nameof(VolumeRow.FxOn)) _states.Notify(MasterFxId);
    }

    private void NotifyScanned()
    {
        _states.Notify(DefaultSpeakerId);
        _states.Notify(MicMutedId);
    }

    private void ForgetStates()
    {
        _master.PropertyChanged -= OnMasterValue;
        _mixer.Updated -= NotifyScanned;
        _states.Unregister(StateIds);
    }
}
