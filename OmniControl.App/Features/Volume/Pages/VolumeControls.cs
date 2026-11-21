using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd;
using OmniControl.App.Features.States;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Volume.Pages;

/// What the page itself does for four of the Volume actions; the module runs each on the page, or on its setting while the page is not made.
public sealed record VolumeViewCalls(Action ToggleView, Action ToggleClosed, Action NextSoundPreset, Action RestoreDefaults);

/// The Volume page's service part, made by the module at start-up so the page is made only when it is first needed: the master's and the
/// default microphone's level and mute the states show (kept in step with Windows by every mixer scan, Windows' own change reports and the
/// level actions), Windows' Listen of a microphone, the master's device, and the page's entries in the master lists (actions, states, OSD
/// elements, the Volume presets' group). The page's rows write through it and follow <see cref="MasterShown"/>, <see cref="InputShown"/>,
/// <see cref="ComboShown"/> and <see cref="ListenChanged"/>. UI thread.
public sealed class VolumeControls : IVolumeLevels, IDisposable
{
    /// The Volume presets' group id (part of its presets' action ids: never rename) and name.
    public const string PresetsId = "volume.presets", PresetsGroupName = "Volume presets";

    private const string MicrophoneElement = OsdService.MicrophoneElement, DefaultOutputWords = "Default speaker", NoCombinedMicrophone = "No combined microphone yet";

    /// The words "Use microphones" is found by (its action and the page's search entry).
    public const string UseMicrophonesKeywords = "use microphones mic mics recording privacy microphone in use icon indicator capture meter listen effects off";

    /// The scope chips' noun for the combined microphone actions.
    private const string ComboNoun = "combined microphones";

    private const string MicIcon = Icons.IconLibrary.Mic, FxIcon = Icons.IconLibrary.Sliders;
    private static readonly OnOffFamily MicListenFamily = new("volume.mic.listen", "Mic listen", VolumeStates.MicListenId);

    private readonly MixerService _mixer;
    private readonly IOsd _osd;
    private readonly IStateRegistry _states;
    private readonly IActionRegistry _hotkeys;
    private readonly IPresetGroups _presetGroups;
    private readonly Action<string> _report;
    private readonly string _title;
    private readonly VolumeActions _actions;
    private readonly List<HotkeyAction> _comboHotkeys = new();
    private VolumeStates? _volumeStates;
    private VolumeMedia? _media;
    private string? _listenFailed;
    private bool _masterFx;

    /// <param name="title">The Volume page's title: the category of its actions and states.</param>
    public VolumeControls(MixerService mixer, IOsd osd, IStateRegistry states, IActionRegistry hotkeys, IPresetGroups presetGroups, VolumePresets presets,
        Action<string> report, string title)
    {
        (_mixer, _osd, _states, _hotkeys, _presetGroups, _report, _title) = (mixer, osd, states, hotkeys, presetGroups, report, title);
        _actions = new VolumeActions(mixer, osd, states, report, this);
        Presets = new VolumePresetProvider(presets);
    }

    /// The Volume presets (the page's preset picker lists them).
    public IPresetProvider Presets { get; }

    public int MasterVolume { get; private set; }
    public bool MasterMuted { get; private set; }

    /// The default recording device that has a Microphones row (a virtual cable has none): its id and name; null: none.
    public string? MicId { get; private set; }
    public string MicName { get; private set; } = string.Empty;
    public bool MicMuted { get; private set; }
    public bool MicListening { get; private set; }
    private int _micVolume;

    /// A level action or Windows' own report changed the master's level or mute (not a scan: the page reads those itself).
    public event Action? MasterShown;

    /// A level action or Windows' own report changed a microphone's level and mute (device id, level, muted).
    public event Action<string, int, bool>? InputShown;

    /// A level action changed a combined microphone's level and mute (its key, level, muted).
    public event Action<string, int, bool>? ComboShown;

    /// Listen of this microphone (device id) was changed here: its row reads it again.
    public event Action<string>? ListenChanged;

    /// True while a change OmniControl made to the master, or to a microphone, is recent enough that Windows' report of it may be its echo.
    public bool MasterJustChanged => _actions.MasterJustChanged;
    public bool InputJustChanged => _actions.InputJustChanged;

    /// Puts the page's entries in the master lists at the page's place, in the order they always had, and starts following Windows.
    public void Register(VolumeViewCalls view)
    {
        Read();
        RegisterHotkeys(view.ToggleClosed);
        RegisterListen();
        _hotkeys.Register(new HotkeyAction("volume.mics.use", "Toggle Use microphones", _title, UseMicrophonesKeywords,
            _ => OnUi(() => _mixer.SetMicrophones(!_mixer.MicrophonesOn))) { Icon = MicIcon });
        _presetGroups.Register(new PresetGroup(PresetsId, _title, PresetsGroupName, Presets));
        RegisterViewActions(view);
        _mixer.Updated += OnScanned; // before the states' own handler, which notifies what this read
        _mixer.MasterDeviceChanged += Read; // the master's level is the new device's
        _mixer.FxChanged += OnFxChanged;
        _osd.VolumeWatched += OnMasterWatched;
        _osd.MicrophoneWatched += OnMicrophoneWatched;
        _volumeStates = new VolumeStates(_states, _mixer, this, _title);
        _media = new VolumeMedia(_hotkeys, _states, _title);
    }

    // ---- the levels ----

    /// What Windows says now of the master and the default microphone (a scan; a level just set here is kept, so an older echo never moves it back).
    private void Read()
    {
        if (!MasterJustChanged)
        {
            CoreAudio.GetMaster(out var level, out var muted, _mixer.MasterDeviceId);
            _actions.RememberMaster(level);
            Master(level, muted);
        }
        SyncMasterFx();
        var mic = _mixer.RecordingDevices.FirstOrDefault(d => d.IsDefault && !VirtualCables.IsCable(d.Name));
        (MicId, MicName) = (mic?.Id, mic?.Name ?? string.Empty);
        if (mic is null) return;
        if (!InputJustChanged && CoreAudio.GetDeviceVolume(mic.Id, out var v, out var m)) (_micVolume, MicMuted) = (v, m);
        if (CoreAudio.GetListen(mic.Id, out var on, out _)) MicListening = on;
    }

    private void OnScanned()
    {
        Read();
        SyncHotkeyAvailability();
    }

    private void OnFxChanged(string? key)
    {
        if (key is null) SyncMasterFx();
    }

    private void SyncMasterFx()
    {
        var on = _mixer.IsFxOn(null);
        if (on == _masterFx) return;
        _masterFx = on;
        if (_volumeStates is not null) _states.Notify(VolumeStates.MasterFxId);
    }

    /// The master's level and mute as the states show them; each change is said once (from the registration on, as the page's row did).
    private void Master(int level, bool muted)
    {
        var (volumeChanged, muteChanged) = (level != MasterVolume, muted != MasterMuted);
        (MasterVolume, MasterMuted) = (level, muted);
        if (_volumeStates is null) return;
        if (volumeChanged)
        {
            _states.Notify(VolumeStates.MasterVolumeId);
            _states.Notify(VolumeStates.AllSpeakersId);
        }
        if (muteChanged) _states.Notify(VolumeStates.MasterMutedId);
    }

    void IVolumeLevels.ShowMaster(int level, bool muted)
    {
        Master(level, muted);
        MasterShown?.Invoke();
    }

    void IVolumeLevels.ShowInput(string deviceId, int level, bool muted)
    {
        if (deviceId == MicId) (_micVolume, MicMuted) = (level, muted);
        InputShown?.Invoke(deviceId, level, muted);
    }

    void IVolumeLevels.ShowCombo(string key, int level, bool muted) => ComboShown?.Invoke(key, level, muted);

    // Windows says the master or the microphone changed (the OSD's own source): the levels take it at once and the states notify, so the
    // tray tiles do not wait for the next scan. A level just set here is kept, as the scan does.
    private void OnMasterWatched(int level, bool muted) => Dispatcher.UIThread.Post(() =>
        ((IVolumeLevels)this).ShowMaster(MasterJustChanged ? MasterVolume : level, muted));

    private void OnMicrophoneWatched(int level, bool muted) => Dispatcher.UIThread.Post(() =>
    {
        if (MicId is not { } id) return;
        ((IVolumeLevels)this).ShowInput(id, InputJustChanged ? _micVolume : level, muted);
        _states.Notify(VolumeStates.MicMutedId);
        _states.Notify(VolumeStates.MicListenId);
    });

    /// The master row's level, pan and mute, also the states': remembered for the next step, written to Windows.
    public void SetMasterVolume(int level)
    {
        _actions.NoteMasterChange(level);
        CoreAudio.SetMasterVolume(level, _mixer.MasterDeviceId);
        ((IVolumeLevels)this).ShowMaster(level, MasterMuted);
    }

    public void SetMasterPan(int pan)
    {
        _actions.NoteMasterChange();
        CoreAudio.SetMasterPan(pan, _mixer.MasterDeviceId);
    }

    public void SetMasterMute(bool muted)
    {
        _actions.NoteMasterChange();
        CoreAudio.SetMasterMute(muted, _mixer.MasterDeviceId);
        ((IVolumeLevels)this).ShowMaster(MasterVolume, muted);
    }

    /// A microphone row's level and mute.
    public void SetInputVolume(string deviceId, int level)
    {
        _actions.NoteInputChange();
        CoreAudio.SetDeviceVolume(deviceId, level);
        if (deviceId == MicId) _micVolume = level;
    }

    public void SetInputMute(string deviceId, bool muted)
    {
        _actions.NoteInputChange();
        CoreAudio.SetDeviceMute(deviceId, muted);
        if (deviceId != MicId) return;
        MicMuted = muted;
        _states.Notify(VolumeStates.MicMutedId);
    }

    /// The microphone mute state's Set: the same as its action.
    public void ToggleMicMute() => _actions.StepInput(0, toggleMute: true);

    /// What the microphone state tells the tray tile while the mute key's result is checked.
    public StateLiveness MicLiveness() => _actions.MicLiveness();

    /// The master's real device becomes this device: the bus's output while it owns Windows' default, else Windows' default itself.
    public void MakeMasterDevice(string target)
    {
        if (_mixer.MasterBusOwnsDefault && !CombinedDevice.IsKey(target)) _mixer.SetRealMasterDevice(target);
        else MakeDefault(target);
    }

    /// Windows' default device becomes this device, or a combined device's cable (everything sent there plays on its members).
    private void MakeDefault(string target)
    {
        if (_mixer.CombinedOf(target) is not { } combo) _mixer.SetDefaultDevice(target);
        else if (combo.Cable.Length > 0) _mixer.SetDefaultDevice(combo.Cable);
        else _report($"{combo.Name} has no virtual cable yet ({EffectsRules.NeedsAnotherCable.ToLowerInvariant()}).");
    }

    // ---- Listen: Windows' own "Listen to this device", so Windows plays the input itself and OmniControl adds no delay ----

    /// Windows' Listen of one microphone: on or off, and the name of the output it plays on; null when Windows does not say.
    public (bool On, string Output)? ReadListen(string deviceId) =>
        CoreAudio.GetListen(deviceId, out var on, out var output) ? (on, OutputName(output)) : null;

    private string OutputName(string? outputId) =>
        outputId is null
            ? $"{DefaultOutputWords} · {_mixer.Devices.FirstOrDefault(d => d.IsDefault)?.Name ?? "none"}"
            : _mixer.Devices.FirstOrDefault(d => d.Id == outputId)?.Name ?? "an output that is not connected";

    /// Turns listening on or off, keeping the output (<paramref name="keepOutput"/>) or setting a new one; says once when Windows refuses.
    public void SetListen(string deviceId, string name, bool on, string? outputId, bool keepOutput)
    {
        if (!_mixer.MicrophonesOn)
        {
            _report($"{EffectsRules.MicrophonesOffLine}.");
            return;
        }
        if (keepOutput) CoreAudio.GetListen(deviceId, out _, out outputId);
        if (!CoreAudio.TrySetListen(deviceId, on, outputId, out var error))
        {
            if (_listenFailed != error)
            {
                _report($"Windows did not change Listen for {name}.");
                Guard.Log("Mic listen", new InvalidOperationException(error));
            }
            _listenFailed = error;
        }
        else _listenFailed = null;
        if (deviceId == MicId && CoreAudio.GetListen(deviceId, out var now, out _)) MicListening = now;
        ListenChanged?.Invoke(deviceId);
        _states.Notify(VolumeStates.MicListenId);
    }

    /// The Listen button's menu: the default output first, then each output; the ticked one is where it plays now.
    public IReadOnlyList<MenuRowItem> ListenRows(string deviceId, string name)
    {
        CoreAudio.GetListen(deviceId, out var on, out var current);
        var rows = new List<MenuRowItem> { MenuRowItem.Header("Listen on") };
        rows.Add(new MenuRowItem(OutputName(null), string.Empty, current is null, string.Empty, new RelayCommand(() => SetListen(deviceId, name, on, null, false))));
        foreach (var d in _mixer.Devices)
            rows.Add(new MenuRowItem(d.Name, string.Empty, current == d.Id, string.Empty, new RelayCommand(() => SetListen(deviceId, name, on, d.Id, false))));
        return rows;
    }

    private void SetDefaultListen(bool? on) => Dispatcher.UIThread.Post(() =>
    {
        if (MicId is { } id) SetListen(id, MicName, on ?? !MicListening, null, keepOutput: true);
    });

    private void RegisterListen()
    {
        var toggle = new HotkeyAction(MicListenFamily.Id, "Toggle mic listen", _title, "sound audio input microphone mic listen monitor hear myself playback",
            _ => SetDefaultListen(null)) { Icon = "headphones", Family = MicListenFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        _hotkeys.Register(MicListenFamily.Siblings(toggle, on => SetDefaultListen(on)).Prepend(toggle));
        _states.Register(new[]
        {
            new StateEntry(VolumeStates.MicListenId, "Mic listen", _title, StateKind.OnOff,
                () => MicId is not null ? StateValue.Of(MicListening, DateTime.Now, VolumeStates.DeviceSubject("Default microphone", MicName)) : null,
                "Windows' own \"Listen to this device\" of the default microphone, read from Windows on every scan.",
                Keywords: "sound audio input microphone mic listen monitor hear", Short: "Listen", Icon: "headphones",
                SuggestedIcons: new[] { "headphones", MicIcon }, Set: value => SetDefaultListen(VolumeStates.IsOn(value))),
        });
    }

    // ---- the actions (what their steps do to Windows is VolumeActions) ----

    private void RegisterHotkeys(Action toggleClosed)
    {
        _osd.Declare(OsdService.VolumeElement, OsdLevelIcon.Speaker);
        _osd.Declare(MicrophoneElement, OsdLevelIcon.Microphone);

        var mic = new HotkeyLevel("mic", "Microphone level");
        var volume = new HotkeyLevel("volume", "Volume");
        var micMute = new HotkeyAction("volume.mic.mute", "Toggle microphone mute", _title, "sound audio input microphone mic mute silence",
            _ => _actions.StepInput(0, toggleMute: true)) { Icon = MicIcon, Editor = _actions.MicMuteEditor(), Family = VolumeStates.MicMuteFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        _hotkeys.Register(VolumeStates.MicMuteFamily.Siblings(micMute, muted => _actions.StepInput(0, toggleMute: true, muteTo: muted)).Prepend(micMute));
        _hotkeys.Register(new HotkeyAction("volume.mic.fx", "Toggle microphone FX", _title, "sound audio input microphone mic fx effects chain", _ => ToggleMicFx()) { Icon = FxIcon });
        _hotkeys.Register(HotkeyAction.ForLevel("volume.mic.up", "Microphone level up", _title, "sound audio input microphone mic louder", mic, +1, (_, delta) => _actions.StepInput(delta), icon: MicIcon));
        _hotkeys.Register(HotkeyAction.ForLevel("volume.mic.down", "Microphone level down", _title, "sound audio input microphone mic quieter", mic, -1, (_, delta) => _actions.StepInput(delta), icon: MicIcon));

        // Combined microphones: one action per control over every combined microphone (the user picks all or some; the Shortcuts page
        // reads the device source each time it is shown).
        var combo = new HotkeyLevel("combomic", "Combined microphone level");
        _comboHotkeys.Add(new HotkeyAction("volume.combomic.mute", "Toggle combined microphone mute", _title,
            "sound audio input combined microphone mic mics mix mute silence", ids => OnUi(() => _actions.StepCombos(ids, 0, toggleMute: true)),
            ComboNoun, ComboDevices) { Icon = MicIcon });
        _comboHotkeys.Add(HotkeyAction.ForLevel("volume.combomic.up", "Combined microphone level up", _title,
            "sound audio input combined microphone mic mics mix louder", combo, +1, (ids, delta) => OnUi(() => _actions.StepCombos(ids, delta)), ComboNoun, ComboDevices, icon: MicIcon));
        _comboHotkeys.Add(HotkeyAction.ForLevel("volume.combomic.down", "Combined microphone level down", _title,
            "sound audio input combined microphone mic mics mix quieter", combo, -1, (ids, delta) => OnUi(() => _actions.StepCombos(ids, delta)), ComboNoun, ComboDevices, icon: MicIcon));
        _hotkeys.Register(_comboHotkeys);

        _hotkeys.Register(HotkeyAction.ForLevel("volume.up", "Volume up", _title, "sound audio master louder", volume, +1, (_, delta) => _actions.StepMaster(delta), keyEquivalent: "VolumeUp"));
        _hotkeys.Register(HotkeyAction.ForLevel("volume.down", "Volume down", _title, "sound audio master quieter", volume, -1, (_, delta) => _actions.StepMaster(delta), keyEquivalent: "VolumeDown"));
        var mute = new HotkeyAction("volume.mute", "Toggle mute", _title, "sound audio master mute silence", _ => _actions.ToggleMasterMute())
            { SuggestedIcons = new[] { "mute", "volume" }, Family = VolumeStates.MuteFamily.Family, FamilyRole = OnOffFamily.ToggleRole, KeyEquivalent = "VolumeMute" };
        _hotkeys.Register(VolumeStates.MuteFamily.Siblings(mute, muted => _actions.ToggleMasterMute(muted)).Prepend(mute));
        _hotkeys.Register(new HotkeyAction("volume.closed.toggle", "Show or hide closed programs", _title, "sound audio mixer programs apps closed not open remembered list show hide",
            _ => OnPage("Volume: show or hide closed programs", toggleClosed)));
        var masterFx = new HotkeyAction("volume.fx", "Toggle master FX", _title, "sound audio master fx effects chain equaliser",
            _ => _mixer.SetFxByUser(null, !_mixer.IsFxOn(null)))
            { Icon = FxIcon, SuggestedIcons = new[] { FxIcon, "eq", "wave" }, Family = VolumeStates.MasterFxFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        _hotkeys.Register(VolumeStates.MasterFxFamily.Siblings(masterFx, on => _mixer.SetFxByUser(null, on)).Prepend(masterFx));
        SyncHotkeyAvailability();
    }

    private void RegisterViewActions(VolumeViewCalls view)
    {
        _hotkeys.Register(new HotkeyAction("volume.view.toggle", "Switch Volume view (Easy / Advanced)", _title, "sound audio volume easy advanced simple view mode switch",
            _ => OnPage("Volume: switch view", view.ToggleView)));
        _hotkeys.Register(new HotkeyAction("volume.easy.sound.next", "Next sound preset for everything", _title,
            "sound audio master easy preset bass boost vocal night loud equaliser eq next", _ => OnPage("Volume: next sound preset", view.NextSoundPreset)));
        _hotkeys.Register(new HotkeyAction("volume.easy.restore", "Restore audio defaults", _title,
            "reset default audio restore sound effects off undo easy", _ => OnPage("Volume: restore audio defaults", view.RestoreDefaults)));
    }

    /// The default microphone's effects on or off, as its row's FX switch does.
    private void ToggleMicFx() => OnUi(() =>
    {
        if (MicId is not { } id) return;
        _mixer.SetFxByUser(_mixer.InputKey(id, MicName), !_mixer.IsFxOn(MixerService.InputPrefix + id));
    });

    /// The reason each state-dependent action does nothing now, or none: the combined microphone actions while there is none. Kept live like every gate.
    private void SyncHotkeyAvailability()
    {
        var none = _mixer.Combined.Any(c => c.Kind == CombinedKind.Microphones) ? null : NoCombinedMicrophone;
        foreach (var action in _comboHotkeys) action.UnavailableReason = none;
    }

    /// The combined microphones a hotkey can act on (their stored keys and names).
    private IReadOnlyList<HotkeyDevice> ComboDevices() =>
        _mixer.Combined.Where(c => c.Kind == CombinedKind.Microphones).Select(c => new HotkeyDevice(c.Key, c.Name)).ToList();

    /// Hotkeys may arrive off the UI thread; the mixer's settings live on it.
    private static void OnUi(Action action) => Dispatcher.UIThread.Post(action);

    /// The same for what only the page does (the page may be made for it, which can fail like any page's making).
    private static void OnPage(string what, Action action) => OnUi(() => Guard.Run(what, action));

    public void Dispose()
    {
        _mixer.Updated -= OnScanned;
        _mixer.MasterDeviceChanged -= Read;
        _mixer.FxChanged -= OnFxChanged;
        _osd.VolumeWatched -= OnMasterWatched;
        _osd.MicrophoneWatched -= OnMicrophoneWatched;
        _volumeStates?.Dispose();
        _media?.Dispose();
    }
}
