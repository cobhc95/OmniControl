using System.ComponentModel;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd;
using OmniControl.App.Features.States;
using OmniControl.App.Features.Volume.Audio;
using IconLibrary = OmniControl.App.Features.Icons.IconLibrary;

namespace OmniControl.App.Features.Volume.Pages;

/// The Volume page's states in the master list of states: the master's volume, mute and effects (what its row shows), Windows' default
/// playback device, every speaker's level, the default microphone's mute and "Keep OmniControl's effects in place". The master row is kept
/// in step with Windows by every scan, so its values are what is read; a change of any of them notifies at once. Each can be set from outside
/// the page (the tray flyout) the way the page's own control does. The Mic listen state is the page's (its Listen button); it is forgotten here with the rest.
internal sealed class VolumeStates : IDisposable
{
    public const string MasterVolumeId = "volume.master.level", MasterMutedId = "volume.master.muted", MasterFxId = "volume.master.fx",
        DefaultSpeakerId = "volume.default.speaker", AllSpeakersId = "volume.speakers.all.level", MicMutedId = "volume.mic.muted", KeepFxId = "volume.fx.keepinplace",
        MicListenId = "volume.mic.listen";

    // The On / Off / Toggle families of the actions that set these states (each action's StateId is the state it sets).
    public static readonly OnOffFamily MuteFamily = new("volume.mute", "Mute", MasterMutedId);
    public static readonly OnOffFamily MicMuteFamily = new("volume.mic.mute", "Microphone mute", MicMutedId);
    public static readonly OnOffFamily MasterFxFamily = new("volume.fx", "Master FX", MasterFxId);
    private static readonly string[] StateIds = { MasterVolumeId, MasterMutedId, MasterFxId, DefaultSpeakerId, AllSpeakersId, MicMutedId, KeepFxId, MicListenId };

    private const string ScanWords = "Windows' own value: Windows reports every change as it happens (the volume pop-up's source), and it is looked at every few seconds as a backstop.";
    private const string FxWords = "What the master row's FX switch shows.";

    private readonly IStateRegistry _states;
    private readonly MixerService _mixer;
    private readonly IOsd _osd;
    private readonly VolumeActions _actions;
    private readonly IVolumeRows _rows;

    /// Registers the states; the page's master row exists by then.
    public VolumeStates(IStateRegistry states, MixerService mixer, IOsd osd, VolumeActions actions, IVolumeRows rows, string title)
    {
        _states = states;
        _mixer = mixer;
        _osd = osd;
        _actions = actions;
        _rows = rows;
        var master = rows.Master;
        states.Register(new[]
        {
            new StateEntry(MasterVolumeId, "Master volume", title, StateKind.Level, () => StateValue.Of(master.Volume, DateTime.Now), ScanWords,
                Unit: "%", Keywords: "sound level loud quiet", Set: value => { if (value.Number is { } level) master.Volume = (int)Math.Round(level); },
                Short: "Volume", SuggestedIcons: new[] { "volume", "volume-low", "speaker" }, Common: CommonGroups.Sound),
            new StateEntry(MasterMutedId, "Sound muted", title, StateKind.OnOff, () => StateValue.Of(master.Muted, DateTime.Now, DeviceSubject("Default speaker", mixer.Devices.FirstOrDefault(d => d.IsDefault)?.Name)), ScanWords,
                Keywords: "sound mute silent", Set: value => master.Muted = IsOn(value), Short: "Mute", Compact: "Sound", StateIcons: new OnOffIcons("mute", "volume"), SuggestedIcons: new[] { "mute", "volume" }, Common: CommonGroups.Sound),
            new StateEntry(MasterFxId, "Master effects on", title, StateKind.OnOff, () => StateValue.Of(master.FxOn, DateTime.Now), FxWords,
                Keywords: "sound fx effects chain equaliser", Set: value => mixer.SetFxByUser(null, IsOn(value)), Short: "FX", Icon: IconLibrary.Sliders, SuggestedIcons: new[] { IconLibrary.Sliders, "eq", "wave" }),
            new StateEntry(DefaultSpeakerId, "Default speaker", title, StateKind.Choice,
                () => mixer.Devices.FirstOrDefault(d => d.IsDefault) is { } device ? StateValue.Of(device.Name, DateTime.Now) : null, ScanWords,
                Choices: () => mixer.Devices.Select(d => d.Name).Distinct().ToList(), Keywords: "sound output playback device headphones",
                Set: value => { if (mixer.Devices.FirstOrDefault(d => d.Name == value.Text) is { } device) rows.MakeMasterDevice(device.Id); }, Common: CommonGroups.Sound),
            new StateEntry(AllSpeakersId, "All speakers volume", title, StateKind.Level, () => StateValue.Of(master.Volume, DateTime.Now),
                "Reads the default speaker; setting it sets every output device to the same level.", Unit: "%", Keywords: "sound level loud quiet every output device speakers",
                Set: SetAllSpeakers, Short: "All speakers", SuggestedIcons: new[] { "volume", "speaker" }),
            new StateEntry(MicMutedId, "Mic muted", title, StateKind.OnOff,
                () => rows.DefaultInput() is { } input ? StateValue.Of(input.Muted, DateTime.Now, DeviceSubject("Default microphone", mixer.RecordingDevices.FirstOrDefault(d => d.IsDefault)?.Name)) : null, ScanWords,
                Keywords: "sound audio input microphone mic mute silence", Short: "Mic mute", Compact: "Mic", StateIcons: new OnOffIcons("mic-off", IconLibrary.Mic), Icon: IconLibrary.Mic, SuggestedIcons: new[] { IconLibrary.Mic, "mic-off" }, Liveness: actions.MicLiveness, Common: CommonGroups.Sound,
                Set: value => { if (rows.DefaultInput() is { } input && input.Muted != IsOn(value)) actions.StepInput(0, toggleMute: true); }),
            new StateEntry(KeepFxId, EffectsRules.KeepInPlace, title, StateKind.OnOff, () => StateValue.Of(mixer.KeepFxInPlace), "Read from the setting: on puts OmniControl's line back into Equalizer APO's config whenever another program takes it out.",
                Keywords: "equalizer apo config keep effects in place restore put back include", Set: value => mixer.SetKeepFxInPlace(IsOn(value)), Short: "Keep FX", SuggestedIcons: new[] { IconLibrary.Sliders }),
        });
        master.PropertyChanged += OnMasterValue;
        mixer.Updated += NotifyScanned;
        mixer.KeepFxInPlaceChanged += NotifyKeepFx;
        osd.VolumeWatched += OnMasterWatched;
        osd.MicrophoneWatched += OnMicrophoneWatched;
    }

    /// What a state says about the device it acts on ("Default speaker: Speakers"); null when there is no name.
    public static string? DeviceSubject(string role, string? name) => string.IsNullOrEmpty(name) ? null : $"{role}: {name}";

    public static bool IsOn(StateValue value) => value.Number is { } number && number != 0;

    // Windows says the master or the microphone changed (the OSD's own source): the rows take it at once and the states notify, so the tray
    // tiles do not wait for the next scan. A level just set here is kept, as the scan does, so an older echo never moves a slider back.
    private void OnMasterWatched(int level, bool muted) => Dispatcher.UIThread.Post(() =>
    {
        var master = _rows.Master;
        master.Sync(true, _actions.MasterJustChanged ? master.Volume : level, master.Pan, muted, master.FxOn, master.FxCount);
    });

    private void OnMicrophoneWatched(int level, bool muted) => Dispatcher.UIThread.Post(() =>
    {
        if (_rows.DefaultInput() is not { } input) return;
        input.Sync(_actions.InputJustChanged ? input.Volume : level, muted, input.FxOn, input.FxCount);
        _states.Notify(MicMutedId);
        _states.Notify(MicListenId);
    });

    /// Every output device to one level: the default speaker as the master row does, the others off the UI thread (each is a slow Windows call).
    private void SetAllSpeakers(StateValue value)
    {
        if (value.Number is not { } number) return;
        var level = (int)Math.Round(number);
        _rows.Master.Volume = level;
        var others = _mixer.Devices.Where(d => !d.IsDefault).Select(d => d.Id).ToList();
        Task.Run(() => Guard.RunAsync("Set every speaker", () => { foreach (var id in others) CoreAudio.SetDeviceVolume(id, level); return Task.CompletedTask; }));
    }

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
        _states.Notify(MasterMutedId); // its subject names the default speaker
        _states.Notify(MicMutedId);
    }

    private void NotifyKeepFx() => _states.Notify(KeepFxId);

    public void Dispose()
    {
        _mixer.KeepFxInPlaceChanged -= NotifyKeepFx;
        _rows.Master.PropertyChanged -= OnMasterValue;
        _mixer.Updated -= NotifyScanned;
        _osd.VolumeWatched -= OnMasterWatched;
        _osd.MicrophoneWatched -= OnMicrophoneWatched;
        _states.Unregister(StateIds);
    }
}
