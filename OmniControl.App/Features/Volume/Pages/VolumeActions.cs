using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd;
using OmniControl.App.Features.States;
using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages;

/// What the Volume page's level actions do to Windows: master and default-microphone steps and mutes run off the UI thread (Windows audio
/// calls are slow) and the pop-up shows from the remembered level at once; the combined microphones' steps; and when the page itself last
/// changed the master or a microphone, so an older echo never moves a slider back. Also "Mute the way your keyboard does": when the user has
/// taught OmniControl their keyboard's mute key (<see cref="MicMuteKey"/>), the microphone mute sends that key, then the real mute is read
/// again a moment later; if the key changed nothing, Windows' own mute is used. While that second read waits, the microphone state says it
/// is being read (its liveness), which the tray tile shows as its small grey dot.
internal sealed class VolumeActions
{
    private const string MicKeyOptionKey = "keymute", MicrophoneElement = OsdService.MicrophoneElement;

    private readonly MixerService _mixer;
    private readonly IOsd _osd;
    private readonly IStateRegistry _states;
    private readonly IVolumeRows _rows;
    private readonly MicMuteKey _micKey;
    private readonly LevelStepper _steps = new();
    private readonly object _inputStep = new();
    private DateTime _masterChange, _inputChange;
    private int _micChecking;
    private bool _fallbackLogged;

    public VolumeActions(MixerService mixer, IOsd osd, IStateRegistry states, Action<string> report, IVolumeRows rows)
    {
        _mixer = mixer;
        _osd = osd;
        _states = states;
        _rows = rows;
        _micKey = new MicMuteKey(report);
    }

    /// True while a change OmniControl made to the master is recent enough that Windows' report of it may be its echo.
    public bool MasterJustChanged => Grace.Within(_masterChange, Timing.SliderEchoGrace);

    /// The same for the microphones.
    public bool InputJustChanged => Grace.Within(_inputChange, Timing.SliderEchoGrace);

    /// The master's row changed the master (a slider, pan or mute); a level is remembered for the next step.
    public void NoteMasterChange(int? level = null)
    {
        _masterChange = DateTime.UtcNow;
        if (level is { } v) RememberMaster(v);
    }

    /// The master's level as Windows has it now (a scan), so the next step starts there.
    public void RememberMaster(int level) => _steps.Remember(MixerService.MasterKey, level);

    /// OmniControl itself is changing a microphone: the page ignores the echo, and so does the OSD's watcher.
    public void NoteInputChange()
    {
        _inputChange = DateTime.UtcNow;
        _osd.NoteOwnChange(MicrophoneElement);
    }

    // ---- master ----

    /// A hotkey or wheel step of the master.
    public void StepMaster(int delta) => Task.Run(() =>
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
        SyncMaster(level, false);
    }

    private void SyncMaster(int level, bool muted) => Dispatcher.UIThread.Post(() =>
    {
        var master = _rows.Master;
        master.Sync(true, level, master.Pan, muted, master.FxOn, master.FxCount);
    });

    /// Mutes or unmutes the master (<paramref name="to"/>: true = muted), or flips it when null. The pop-up shows before the change is written.
    public void ToggleMasterMute(bool? to = null) => Task.Run(() =>
    {
        var device = _mixer.MasterDeviceId;
        if (!CoreAudio.GetMaster(out var v, out var muted, device)) return;
        var next = to ?? !muted;
        _osd.ShowVolume(v, next);
        _osd.NoteOwnChange(OsdService.VolumeElement);
        CoreAudio.SetMasterMute(next, device);
        _masterChange = DateTime.UtcNow;
        SyncMaster(v, next);
    });

    // ---- microphone ----

    /// Changes the level of, or mutes, the default recording device. Listing the recording devices and changing one are slow Windows calls
    /// that held up drawing the pop-up, so this runs off the UI thread, one step at a time, in order.
    public void StepInput(int delta, bool toggleMute = false, bool? muteTo = null) => Task.Run(() =>
    {
        lock (_inputStep)
        {
            var def = CoreAudio.InputDevices().FirstOrDefault(d => d.IsDefault);
            if (def is null || !CoreAudio.GetDeviceVolume(def.Id, out var v, out var muted)) return;
            NoteInputChange();
            if (toggleMute && MuteByKey(def.Id, v, muteTo ?? !muted)) return;
            if (toggleMute) muted = muteTo ?? !muted;
            else
            {
                if (muted && delta > 0) muted = false;
                v = Percent.Clamp(v + delta);
            }
            _osd.Show(MicrophoneElement, muted ? 0 : v, toggleMute ? (muted ? "Microphone muted" : "Microphone on") : MicrophoneElement);
            CoreAudio.SetDeviceVolume(def.Id, v);
            CoreAudio.SetDeviceMute(def.Id, muted);
            SyncInput(def.Id, v, muted);
        }
    });

    private void SyncInput(string deviceId, int level, bool muted) => Dispatcher.UIThread.Post(() =>
    {
        if (_rows.Input(deviceId) is { } item) item.Sync(level, muted, item.FxOn, item.FxCount);
        _states.Notify(VolumeStates.MicMutedId);
    });

    /// The setting row of the microphone mute action (its live settings pane).
    public ActionEditor MicMuteEditor()
    {
        var option = ActionOption.Toggle(MicKeyOptionKey, "Mute the way your keyboard does", note: "Press your microphone mute key once; muting then sends it, so its light follows")
            .TaggedWhen(() => _micKey.Status);
        return new ActionEditor(new[] { option },
            () => new Dictionary<string, string> { [MicKeyOptionKey] = _micKey.IsOn ? "1" : "0" },
            (key, value) =>
            {
                if (key == MicKeyOptionKey) _micKey.SetEnabled(value == "1");
                return null;
            });
    }

    /// What the microphone state tells the tray tile: nothing is being changed, and it is being read while the key's result is checked.
    public StateLiveness MicLiveness() => new(false, Volatile.Read(ref _micChecking) > 0, null, null);

    /// Sends the learned mute key for the default microphone when one is in use; false means the normal mute must be used. Runs inside <see cref="StepInput"/>'s lock.
    private bool MuteByKey(string deviceId, int level, bool wantMuted)
    {
        if (!_micKey.TrySend()) return false;
        _osd.Show(MicrophoneElement, wantMuted ? 0 : level, wantMuted ? "Microphone muted" : "Microphone on");
        _ = Guard.RunAsync("Checking the microphone mute", () => CheckMicMuteAsync(deviceId, wantMuted));
        return true;
    }

    private async Task CheckMicMuteAsync(string deviceId, bool wantMuted)
    {
        Interlocked.Increment(ref _micChecking);
        _states.NotifyLiveness(VolumeStates.MicMutedId);
        try
        {
            await Task.Delay(Timing.MicKeyCheckDelay).ConfigureAwait(false);
            lock (_inputStep)
            {
                if (!CoreAudio.GetDeviceVolume(deviceId, out var level, out var muted)) return;
                if (muted != wantMuted)
                {
                    if (!_fallbackLogged)
                    {
                        _fallbackLogged = true;
                        AppDataStore.AppendLog(Guard.LogFile, "The microphone mute key did not change the mute; Windows' own mute was used instead.");
                    }
                    NoteInputChange();
                    CoreAudio.SetDeviceMute(deviceId, wantMuted);
                    muted = wantMuted;
                }
                SyncInput(deviceId, level, muted);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _micChecking);
            _states.NotifyLiveness(VolumeStates.MicMutedId);
        }
    }

    // ---- combined microphones ----

    /// Changes the level of, or mutes, the picked combined microphones (all when <paramref name="ids"/> is null); the pop-up shows the
    /// first one's new level. Applied by its run at once (no restart). UI thread.
    public void StepCombos(IReadOnlyList<string>? ids, int delta, bool toggleMute = false)
    {
        var shown = false;
        foreach (var c in _mixer.Combined.Where(c => c.Kind == CombinedKind.Microphones && (ids is null || ids.Contains(c.Key))).ToList())
        {
            var muted = toggleMute ? !c.Muted : c.Muted && delta <= 0;
            var level = Percent.Clamp(c.Volume + delta);
            _osd.NoteOwnChange(MicrophoneElement);
            _mixer.SetCombinedLevel(c.Key, level, muted);
            if (_rows.Combo(c.Key) is { } row) row.Sync(level, muted, row.FxOn, row.FxCount);
            if (shown) continue;
            _osd.Show(MicrophoneElement, muted ? 0 : level, toggleMute ? $"{c.Name} {(muted ? "muted" : "on")}" : c.Name);
            shown = true;
        }
    }
}
