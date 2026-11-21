using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd;

namespace OmniControl.App.Features.Volume.Pages;

/// The page's master-list entries (hotkeys and OSD elements; what their steps do to Windows is <see cref="VolumeActions"/>) and the level meters.
public sealed partial class VolumePageViewModel
{
    /// The actions that do nothing in some states; <see cref="SyncHotkeyAvailability"/> keeps their reason (shown on the Shortcuts page) up to date.
    private readonly List<HotkeyAction> _comboHotkeys = new();

    private const string NoCombinedMicrophone = "No combined microphone yet";

    /// The scope chips' noun for the combined microphone actions.
    private const string ComboNoun = "combined microphones";

    private void RegisterHotkeys(IActionRegistry hotkeys)
    {
        _osd.Declare(OsdService.VolumeElement, OsdLevelIcon.Speaker);
        _osd.Declare(MicrophoneElement, OsdLevelIcon.Microphone);

        var mic = new HotkeyLevel("mic", "Microphone level");
        var volume = new HotkeyLevel("volume", "Volume");
        var micMute = new HotkeyAction("volume.mic.mute", "Toggle microphone mute", Title, "sound audio input microphone mic mute silence",
            _ => _actions.StepInput(0, toggleMute: true)) { Icon = MicIcon, Editor = _actions.MicMuteEditor(), Family = VolumeStates.MicMuteFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        hotkeys.Register(VolumeStates.MicMuteFamily.Siblings(micMute, muted => _actions.StepInput(0, toggleMute: true, muteTo: muted)).Prepend(micMute));
        hotkeys.Register(new HotkeyAction("volume.mic.fx", "Toggle microphone FX", Title, "sound audio input microphone mic fx effects chain",
            _ =>
            {
                if (_mixer.RecordingDevices.FirstOrDefault(d => d.IsDefault) is { } d && _inputs.TryGetValue(d.Id, out var i)) i.FxOn = !i.FxOn;
            }) { Icon = FxIcon });
        hotkeys.Register(HotkeyAction.ForLevel("volume.mic.up", "Microphone level up", Title, "sound audio input microphone mic louder", mic, +1, (_, delta) => _actions.StepInput(delta), icon: MicIcon));
        hotkeys.Register(HotkeyAction.ForLevel("volume.mic.down", "Microphone level down", Title, "sound audio input microphone mic quieter", mic, -1, (_, delta) => _actions.StepInput(delta), icon: MicIcon));

        // Combined microphones: one action per control over every combined microphone (the user picks all or some; the Shortcuts page
        // reads the device source each time it is shown).
        var combo = new HotkeyLevel("combomic", "Combined microphone level");
        _comboHotkeys.Add(new HotkeyAction("volume.combomic.mute", "Toggle combined microphone mute", Title,
            "sound audio input combined microphone mic mics mix mute silence", ids => OnUi(() => _actions.StepCombos(ids, 0, toggleMute: true)),
            ComboNoun, ComboDevices) { Icon = MicIcon });
        _comboHotkeys.Add(HotkeyAction.ForLevel("volume.combomic.up", "Combined microphone level up", Title,
            "sound audio input combined microphone mic mics mix louder", combo, +1, (ids, delta) => OnUi(() => _actions.StepCombos(ids, delta)), ComboNoun, ComboDevices, icon: MicIcon));
        _comboHotkeys.Add(HotkeyAction.ForLevel("volume.combomic.down", "Combined microphone level down", Title,
            "sound audio input combined microphone mic mics mix quieter", combo, -1, (ids, delta) => OnUi(() => _actions.StepCombos(ids, delta)), ComboNoun, ComboDevices, icon: MicIcon));
        hotkeys.Register(_comboHotkeys);

        hotkeys.Register(HotkeyAction.ForLevel("volume.up", "Volume up", Title, "sound audio master louder", volume, +1, (_, delta) => _actions.StepMaster(delta), keyEquivalent: "VolumeUp"));
        hotkeys.Register(HotkeyAction.ForLevel("volume.down", "Volume down", Title, "sound audio master quieter", volume, -1, (_, delta) => _actions.StepMaster(delta), keyEquivalent: "VolumeDown"));
        var mute = new HotkeyAction("volume.mute", "Toggle mute", Title, "sound audio master mute silence", _ => _actions.ToggleMasterMute())
            { SuggestedIcons = new[] { "mute", "volume" }, Family = VolumeStates.MuteFamily.Family, FamilyRole = OnOffFamily.ToggleRole, KeyEquivalent = "VolumeMute" };
        hotkeys.Register(VolumeStates.MuteFamily.Siblings(mute, muted => _actions.ToggleMasterMute(muted)).Prepend(mute));
        hotkeys.Register(new HotkeyAction("volume.closed.toggle", "Show or hide closed programs", Title, "sound audio mixer programs apps closed not open remembered list show hide",
            _ => OnUi(() => { if (Outputs.ProgramTools is { } tools) tools.ShowClosed = !tools.ShowClosed; })));
        var masterFx = new HotkeyAction("volume.fx", "Toggle master FX", Title, "sound audio master fx effects chain equaliser",
            _ => _mixer.SetFxByUser(null, !_mixer.IsFxOn(null)))
            { Icon = FxIcon, SuggestedIcons = new[] { FxIcon, "eq", "wave" }, Family = VolumeStates.MasterFxFamily.Family, FamilyRole = OnOffFamily.ToggleRole };
        hotkeys.Register(VolumeStates.MasterFxFamily.Siblings(masterFx, on => _mixer.SetFxByUser(null, on)).Prepend(masterFx));
        SyncHotkeyAvailability();
    }

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

    // ---- meters ----

    /// The page's view calls these as it is attached and detached. The meters (and the quiet captures behind the microphone meters, the
    /// mixer's close watch and the fine timers) run only while the view is attached AND the page is on screen (the window shown, not
    /// minimised); on the Mixer tab only for groups that are open.
    public void StartMeters()
    {
        _viewAttached = true;
        SyncLive();
    }

    public void StopMeters()
    {
        _viewAttached = false;
        SyncLive();
    }

    public override void OnOnScreenChanged(bool onScreen)
    {
        _onScreen = onScreen;
        SyncLive();
    }

    private bool _viewAttached, _onScreen;
    private IDisposable? _closeWatch;

    private void SyncLive()
    {
        var live = _viewAttached && _onScreen;
        if (live == _meterTimer.IsEnabled) return;
        if (live)
        {
            _closeWatch = _mixer.WatchClosely();
            _meterTimer.Start();
            return;
        }
        _meterTimer.Stop();
        _captures.Keep(Array.Empty<string>());
        foreach (var row in AllRows()) row.Meter.Clear();
        _closeWatch?.Dispose();
        _closeWatch = null;
    }

    private void FeedMeters()
    {
        if (!ShowsMixer || IsEasy) return;
        var now = _clock.Elapsed.TotalMilliseconds;
        if (Outputs.IsExpanded)
        {
            if (_mixer.Peaks(null, out var ml, out var mr)) _master.Meter.Feed(ml, mr, now);
            foreach (var row in _items.Values.Where(i => i.IsOpen))
            {
                _mixer.Peaks(row.Key, out var l, out var r);
                row.Meter.Feed(l, r, now);
            }
        }
        // A recording device's meter only moves while something captures from it, so the open input rows keep a quiet capture
        // ("Use microphones" on only: the capture lights Windows' microphone-in-use icon).
        var micMeters = Inputs.IsExpanded && _mixer.MicrophonesOn;
        _captures.Keep(micMeters ? _inputs.Keys : Array.Empty<string>());
        if (micMeters)
        {
            foreach (var row in _inputs.Values)
            {
                _mixer.InputPeaks(row.Id, out var l, out var r);
                row.Meter.Feed(l, r, now); // a muted mic still shows what it hears
            }
        }
    }
}
