using Avalonia.Threading;
using OmniControl.App.Audio;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Volume;

/// <summary>The page's master-list entries (hotkeys and OSD elements), the master and microphone steps they run, and the level meters.</summary>
public sealed partial class VolumePageViewModel
{
    private readonly LevelStepper _steps = new();
    private readonly object _inputStep = new();

    /// <summary>The actions that do nothing in some states; <see cref="SyncHotkeyAvailability"/> keeps their reason (shown on the Shortcuts page) up to date.</summary>
    private HotkeyAction? _modeHotkey, _micFxHotkey;
    private readonly List<HotkeyAction> _comboHotkeys = new();

    private const string NoCombinedMicrophone = "No combined microphone yet";

    private void RegisterHotkeys(HotkeyRegistry hotkeys)
    {
        _osd.Declare(OsdService.VolumeElement, icon: OsdLevelIcon.Speaker);
        _osd.Declare(MicrophoneElement);

        var mic = new HotkeyLevel("mic", "Microphone level");
        var volume = new HotkeyLevel("volume", "Volume");
        hotkeys.Register(new HotkeyAction("volume.mic.mute", "Toggle microphone mute", Title, "sound audio input microphone mic mute silence",
            _ => StepInput(0, toggleMute: true)));
        _micFxHotkey = new HotkeyAction("volume.mic.fx", "Toggle microphone FX", Title, "sound audio input microphone mic fx effects chain",
            _ =>
            {
                if (_micFxHotkey?.UnavailableReason is { } why) _report(why);
                else if (_mixer.RecordingDevices.FirstOrDefault(d => d.IsDefault) is { } d && _inputs.TryGetValue(d.Id, out var i)) i.FxOn = !i.FxOn;
            });
        hotkeys.Register(_micFxHotkey);
        hotkeys.Register(HotkeyAction.ForLevel("volume.mic.up", "Microphone level up", Title, "sound audio input microphone mic louder", mic, +1, (_, delta) => StepInput(delta)));
        hotkeys.Register(HotkeyAction.ForLevel("volume.mic.down", "Microphone level down", Title, "sound audio input microphone mic quieter", mic, -1, (_, delta) => StepInput(delta)));

        // Combined microphones: one action per control over every combined microphone (the user picks all or some; the Shortcuts page
        // reads the device source each time it is shown).
        var combo = new HotkeyLevel("combomic", "Combined microphone level");
        _comboHotkeys.Add(new HotkeyAction("volume.combomic.mute", "Toggle combined microphone mute", Title,
            "sound audio input combined microphone mic mics mix mute silence", ids => OnUi(() => StepCombos(ids, 0, toggleMute: true)),
            ComboNoun, ComboDevices));
        _comboHotkeys.Add(HotkeyAction.ForLevel("volume.combomic.up", "Combined microphone level up", Title,
            "sound audio input combined microphone mic mics mix louder", combo, +1, (ids, delta) => OnUi(() => StepCombos(ids, delta)), ComboNoun, ComboDevices));
        _comboHotkeys.Add(HotkeyAction.ForLevel("volume.combomic.down", "Combined microphone level down", Title,
            "sound audio input combined microphone mic mics mix quieter", combo, -1, (ids, delta) => OnUi(() => StepCombos(ids, delta)), ComboNoun, ComboDevices));
        hotkeys.Register(_comboHotkeys);

        hotkeys.Register(HotkeyAction.ForLevel("volume.up", "Volume up", Title, "sound audio master louder", volume, +1, (_, delta) => StepMaster(delta)));
        hotkeys.Register(HotkeyAction.ForLevel("volume.down", "Volume down", Title, "sound audio master quieter", volume, -1, (_, delta) => StepMaster(delta)));
        hotkeys.Register(new HotkeyAction("volume.mute", "Toggle mute", Title, "sound audio master mute silence", _ => ToggleMasterMute()));
        hotkeys.Register(new HotkeyAction("volume.closed.toggle", "Show or hide closed programs", Title, "sound audio mixer programs apps closed not open remembered list show hide",
            _ => OnUi(() => { if (Outputs.ProgramTools is { } tools) tools.ShowClosed = !tools.ShowClosed; })));
        hotkeys.Register(new HotkeyAction("volume.fx", "Toggle master FX", Title, "sound audio master fx effects chain equaliser",
            _ => _mixer.SetFxOn(null, !_mixer.IsFxOn(null))));
        // The id is the old "Next master effects mode" one, so keys bound to it keep working.
        _modeHotkey = new HotkeyAction("volume.fxmode.next", "Switch sound mode", Title,
            "sound audio mode effects " + string.Join(' ', SoundModes.All.Select(m => m.Name.ToLowerInvariant())),
            _ => NextSoundMode());
        hotkeys.Register(_modeHotkey);
        SyncHotkeyAvailability();
    }

    /// <summary>
    /// The reason each state-dependent action does nothing now, or none: "Switch sound mode" when the next mode can't be chosen, the
    /// microphone effects when the mode can't run them, the combined microphone actions while there is none. Kept live like every gate.
    /// </summary>
    private void SyncHotkeyAvailability()
    {
        if (_modeHotkey is not null) _modeHotkey.UnavailableReason = SoundModes.NextModeUnavailable(_mixer.SoundMode, _modes.Current);
        if (_micFxHotkey is not null) _micFxHotkey.UnavailableReason = _modes.Check(SoundFeature.MicEffects).Reason;
        var none = _mixer.Combined.Any(c => c.Kind == CombinedKind.Microphones) ? null : NoCombinedMicrophone;
        foreach (var action in _comboHotkeys) action.UnavailableReason = none;
    }

    // ---- master ----

    /// <summary>Hotkey and wheel steps run off the UI thread (Windows audio calls are slow); the pop-up shows from the remembered level at once.</summary>
    private void StepMaster(int delta) => Task.Run(() =>
    {
        if (_steps.Step(MixerService.MasterKey, delta, ReadMaster, level => WriteMaster(level, delta), ShowMaster) is { } level) ShowMaster(level);
    });

    private int? ReadMaster() => CoreAudio.GetMaster(out var level, out _, _mixer.MasterDeviceId) ? level : null;

    private void WriteMaster(int level, int delta)
    {
        var device = _mixer.MasterDeviceId;
        if (delta > 0 && CoreAudio.GetMaster(out _, out var muted, device) && muted) CoreAudio.SetMasterMute(false, device);
        _osd.NoteOwnChange(OsdService.VolumeElement);
        CoreAudio.SetMasterVolume(level, device);
        _masterChange = DateTime.UtcNow;
    }

    private void ShowMaster(int level)
    {
        _osd.ShowVolume(level, muted: false);
        Dispatcher.UIThread.Post(() => _master.Sync(true, level, _master.Pan, false, _master.FxOn, _master.FxCount));
    }

    /// <summary>Off the UI thread like the steps (Windows audio calls are slow); the pop-up shows before the change is written.</summary>
    private void ToggleMasterMute() => Task.Run(() =>
    {
        var device = _mixer.MasterDeviceId;
        if (!CoreAudio.GetMaster(out var v, out var muted, device)) return;
        _osd.ShowVolume(v, !muted);
        _osd.NoteOwnChange(OsdService.VolumeElement);
        CoreAudio.SetMasterMute(!muted, device);
        _masterChange = DateTime.UtcNow;
        Dispatcher.UIThread.Post(() => _master.Sync(true, v, _master.Pan, !muted, _master.FxOn, _master.FxCount));
    });

    // ---- microphone ----

    /// <summary>
    /// Hotkeys: change the level of, or mute, the default recording device. Off the UI thread like the master steps: listing the
    /// recording devices and changing one are slow Windows calls that held up drawing the pop-up. One step at a time, in order.
    /// </summary>
    private void StepInput(int delta, bool toggleMute = false) => Task.Run(() =>
    {
        lock (_inputStep)
        {
            var def = CoreAudio.InputDevices().FirstOrDefault(d => d.IsDefault);
            if (def is null || !CoreAudio.GetDeviceVolume(def.Id, out var v, out var muted)) return;
            _inputChange = DateTime.UtcNow;
            if (toggleMute) muted = !muted;
            else
            {
                if (muted && delta > 0) muted = false;
                v = Percent.Clamp(v + delta);
            }
            _osd.Show(MicrophoneElement, muted ? 0 : v, toggleMute ? (muted ? "Microphone muted" : "Microphone on") : MicrophoneElement);
            CoreAudio.SetDeviceVolume(def.Id, v);
            CoreAudio.SetDeviceMute(def.Id, muted);
            Dispatcher.UIThread.Post(() =>
            {
                if (_inputs.TryGetValue(def.Id, out var item)) item.Sync(v, muted, item.FxOn, item.FxCount);
            });
        }
    });

    // ---- combined microphones ----

    /// <summary>The scope chips' noun for the combined microphone actions.</summary>
    private const string ComboNoun = "combined microphones";

    /// <summary>The combined microphones a hotkey can act on (their stored keys and names).</summary>
    private IReadOnlyList<HotkeyDevice> ComboDevices() =>
        _mixer.Combined.Where(c => c.Kind == CombinedKind.Microphones).Select(c => new HotkeyDevice(c.Key, c.Name)).ToList();

    /// <summary>Hotkeys may arrive off the UI thread; the mixer's settings live on it.</summary>
    private static void OnUi(Action action) => Dispatcher.UIThread.Post(action);

    /// <summary>
    /// Changes the level of, or mutes, the picked combined microphones (all when <paramref name="ids"/> is null); the pop-up shows the
    /// first one's new level. Applied by its run at once (no restart).
    /// </summary>
    private void StepCombos(IReadOnlyList<string>? ids, int delta, bool toggleMute = false)
    {
        var shown = false;
        foreach (var c in _mixer.Combined.Where(c => c.Kind == CombinedKind.Microphones && (ids is null || ids.Contains(c.Key))).ToList())
        {
            var muted = toggleMute ? !c.Muted : c.Muted && delta <= 0;
            var level = Percent.Clamp(c.Volume + delta);
            _mixer.SetCombinedLevel(c.Key, level, muted);
            if (_combos.TryGetValue(c.Key, out var row)) row.Sync(level, muted, row.FxOn, row.FxCount);
            if (shown) continue;
            _osd.Show(MicrophoneElement, muted ? 0 : level, toggleMute ? $"{c.Name} {(muted ? "muted" : "on")}" : c.Name);
            shown = true;
        }
    }

    // ---- meters ----

    /// <summary>The page calls these while it is on screen; the meters only run then, on the Mixer tab, and only for groups that are open.</summary>
    public void StartMeters() => _meterTimer.Start();

    public void StopMeters()
    {
        _meterTimer.Stop();
        foreach (var row in AllRows()) row.Meter.Clear();
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
        if (Inputs.IsExpanded)
        {
            foreach (var row in _inputs.Values)
            {
                CoreAudio.DevicePeaks(row.Id, true, out var l, out var r);
                row.Meter.Feed(row.Muted ? 0 : l, row.Muted ? 0 : r, now);
            }
        }
    }
}
