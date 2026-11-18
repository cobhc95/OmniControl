using System.ComponentModel;
using Avalonia.Threading;
using OmniControl.App.Audio;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.App.States;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

// The page's states in the master list of states: the master's volume, mute and effects (what its row shows), Windows' default playback
// device, the default microphone's mute and "Keep OmniControl's effects in place". The master row is kept in step with Windows by every scan, so its values are what is read;
// a change of any of them notifies at once. Each can be set from outside the page (the tray flyout) the way the page's own control does.
public sealed partial class VolumePageViewModel
{
    private const string MasterVolumeId = "volume.master.level", MasterMutedId = "volume.master.muted", MasterFxId = "volume.master.fx",
        DefaultSpeakerId = "volume.default.speaker", AllSpeakersId = "volume.speakers.all.level", MicMutedId = "volume.mic.muted", KeepFxId = "volume.fx.keepinplace";
    // The On / Off / Toggle families of the actions that set these states (each action's StateId is the state it sets).
    private static readonly OnOffFamily MuteFamily = new("volume.mute", "Mute", MasterMutedId);
    private static readonly OnOffFamily MicMuteFamily = new("volume.mic.mute", "Microphone mute", MicMutedId);
    private static readonly OnOffFamily MasterFxFamily = new("volume.fx", "Master FX", MasterFxId);
    private static readonly string[] StateIds = { MasterVolumeId, MasterMutedId, MasterFxId, DefaultSpeakerId, AllSpeakersId, MicMutedId, KeepFxId };

    private const string ScanWords = "Windows' own value: Windows reports every change as it happens (the volume pop-up's source), and it is looked at every few seconds as a backstop.";
    private const string FxWords = "What the master row's FX switch shows.";

    private void RegisterStates()
    {
        _states.Register(new[]
        {
            new StateEntry(MasterVolumeId, "Master volume", Title, StateKind.Level, () => StateValue.Of(_master.Volume, DateTime.Now), ScanWords,
                Unit: "%", Keywords: "sound level loud quiet", Set: value => { if (value.Number is { } level) _master.Volume = (int)Math.Round(level); },
                Short: "Volume", SuggestedIcons: new[] { "volume", "volume-low", "speaker" }, Common: CommonGroups.Sound),
            new StateEntry(MasterMutedId, "Master muted", Title, StateKind.OnOff, () => StateValue.Of(_master.Muted, DateTime.Now), ScanWords,
                Keywords: "sound mute silent", Set: value => _master.Muted = IsOn(value), Short: "Mute", SuggestedIcons: new[] { "mute", "volume" }, Common: CommonGroups.Sound),
            new StateEntry(MasterFxId, "Master effects on", Title, StateKind.OnOff, () => StateValue.Of(_master.FxOn, DateTime.Now), FxWords,
                Keywords: "sound fx effects chain equaliser", Set: value => _mixer.SetFxByUser(null, IsOn(value)), Short: "FX", Icon: FxIcon, SuggestedIcons: new[] { FxIcon, "eq", "wave" }),
            new StateEntry(DefaultSpeakerId, "Default speaker", Title, StateKind.Choice,
                () => _mixer.Devices.FirstOrDefault(d => d.IsDefault) is { } device ? StateValue.Of(device.Name, DateTime.Now) : null, ScanWords,
                Choices: () => _mixer.Devices.Select(d => d.Name).Distinct().ToList(), Keywords: "sound output playback device headphones",
                Set: value => { if (_mixer.Devices.FirstOrDefault(d => d.Name == value.Text) is { } device) MakeMasterDevice(device.Id); }, Common: CommonGroups.Sound),
            new StateEntry(AllSpeakersId, "All speakers volume", Title, StateKind.Level, () => StateValue.Of(_master.Volume, DateTime.Now),
                "Reads the default speaker; setting it sets every output device to the same level.", Unit: "%", Keywords: "sound level loud quiet every output device speakers",
                Set: SetAllSpeakers, Short: "All speakers", SuggestedIcons: new[] { "volume", "speaker" }),
            new StateEntry(MicMutedId, "Microphone muted", Title, StateKind.OnOff,
                () => DefaultInput() is { } input ? StateValue.Of(input.Muted, DateTime.Now) : null, ScanWords,
                Keywords: "sound audio input microphone mic mute silence", Short: "Mic mute", Icon: MicIcon, SuggestedIcons: new[] { MicIcon, "mic-off" }, Liveness: MicLiveness, Common: CommonGroups.Sound,
                Set: value => { if (DefaultInput() is { } input && input.Muted != IsOn(value)) StepInput(0, toggleMute: true); }),
            new StateEntry(KeepFxId, EffectsRules.KeepInPlace, Title, StateKind.OnOff, () => StateValue.Of(_mixer.KeepFxInPlace), "Read from the setting: on puts OmniControl's line back into Equalizer APO's config whenever another program takes it out.",
                Keywords: "equalizer apo config keep effects in place restore put back include", Set: value => _mixer.SetKeepFxInPlace(IsOn(value)), Short: "Keep FX", SuggestedIcons: new[] { FxIcon }),
        });
        _master.PropertyChanged += OnMasterValue;
        _mixer.Updated += NotifyScanned;
        _mixer.KeepFxInPlaceChanged += NotifyKeepFx;
        _osd.VolumeWatched += OnMasterWatched;
        _osd.MicrophoneWatched += OnMicrophoneWatched;
    }

    // Windows says the master or the microphone changed (the OSD's own source): the rows take it at once and the states notify, so the tray
    // tiles do not wait for the next scan. A level just set here is kept, as the scan does, so an older echo never moves a slider back.
    private void OnMasterWatched(int level, bool muted) => Dispatcher.UIThread.Post(() =>
        _master.Sync(true, Grace.Within(_masterChange, Timing.SliderEchoGrace) ? _master.Volume : level, _master.Pan, muted, _master.FxOn, _master.FxCount));

    private void OnMicrophoneWatched(int level, bool muted) => Dispatcher.UIThread.Post(() =>
    {
        if (DefaultInput() is not { } input) return;
        input.Sync(Grace.Within(_inputChange, Timing.SliderEchoGrace) ? input.Volume : level, muted, input.FxOn, input.FxCount);
        _states.Notify(MicMutedId);
    });

    /// <summary>Every output device to one level: the default speaker as the master row does, the others off the UI thread (each is a slow Windows call).</summary>
    private void SetAllSpeakers(StateValue value)
    {
        if (value.Number is not { } number) return;
        var level = (int)Math.Round(number);
        _master.Volume = level;
        var others = _mixer.Devices.Where(d => !d.IsDefault).Select(d => d.Id).ToList();
        Task.Run(() => Guard.RunAsync("Set every speaker", () => { foreach (var id in others) CoreAudio.SetDeviceVolume(id, level); return Task.CompletedTask; }));
    }

    private static bool IsOn(StateValue value) => value.Number is { } number && number != 0;

    /// <summary>The default recording device's row, or null when there is none.</summary>
    private InputItem? DefaultInput() =>
        _mixer.RecordingDevices.FirstOrDefault(d => d.IsDefault) is { } device && _inputs.TryGetValue(device.Id, out var input) ? input : null;

    private void OnMasterValue(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VolumeRow.Volume))
        {
            _states.Notify(MasterVolumeId);
            _states.Notify(AllSpeakersId);
        }
        else if (e.PropertyName == nameof(VolumeRow.Muted)) _states.Notify(MasterMutedId);
        else if (e.PropertyName == nameof(VolumeRow.FxOn)) _states.Notify(MasterFxId);
    }

    private void NotifyScanned()
    {
        _states.Notify(DefaultSpeakerId);
        _states.Notify(MicMutedId);
    }

    private void NotifyKeepFx() => _states.Notify(KeepFxId);

    private void ForgetStates()
    {
        _mixer.KeepFxInPlaceChanged -= NotifyKeepFx;
        _master.PropertyChanged -= OnMasterValue;
        _mixer.Updated -= NotifyScanned;
        _osd.VolumeWatched -= OnMasterWatched;
        _osd.MicrophoneWatched -= OnMicrophoneWatched;
        _states.Unregister(StateIds);
    }
}
