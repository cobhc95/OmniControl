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

    private void RegisterHotkeys(HotkeyRegistry hotkeys)
    {
        _osd.Declare(OsdService.VolumeElement, icon: OsdLevelIcon.Speaker);
        _osd.Declare(MicrophoneElement);

        var mic = new HotkeyLevel("mic", "Microphone level");
        var volume = new HotkeyLevel("volume", "Volume");
        hotkeys.Register(new HotkeyAction("volume.mic.mute", "Toggle microphone mute", Title, "sound audio input microphone mic mute silence",
            _ => StepInput(0, toggleMute: true)));
        hotkeys.Register(new HotkeyAction("volume.mic.fx", "Toggle microphone FX", Title, "sound audio input microphone mic fx effects chain",
            _ => { if (_mixer.RecordingDevices.FirstOrDefault(d => d.IsDefault) is { } d && _inputs.TryGetValue(d.Id, out var i)) i.FxOn = !i.FxOn; }));
        hotkeys.Register(HotkeyAction.ForLevel("volume.mic.up", "Microphone level up", Title, "sound audio input microphone mic louder", mic, +1, (_, delta) => StepInput(delta)));
        hotkeys.Register(HotkeyAction.ForLevel("volume.mic.down", "Microphone level down", Title, "sound audio input microphone mic quieter", mic, -1, (_, delta) => StepInput(delta)));

        hotkeys.Register(HotkeyAction.ForLevel("volume.up", "Volume up", Title, "sound audio master louder", volume, +1, (_, delta) => StepMaster(delta)));
        hotkeys.Register(HotkeyAction.ForLevel("volume.down", "Volume down", Title, "sound audio master quieter", volume, -1, (_, delta) => StepMaster(delta)));
        hotkeys.Register(new HotkeyAction("volume.mute", "Toggle mute", Title, "sound audio master mute silence", _ => ToggleMasterMute()));
        hotkeys.Register(new HotkeyAction("volume.fx", "Toggle master FX", Title, "sound audio master fx effects chain equaliser",
            _ => _mixer.SetFxOn(null, !_mixer.IsFxOn(null))));
        // The id is the old "Next master effects mode" one, so keys bound to it keep working.
        hotkeys.Register(new HotkeyAction("volume.fxmode.next", "Switch sound mode", Title,
            "sound audio mode effects " + string.Join(' ', SoundModes.All.Select(m => m.Name.ToLowerInvariant())),
            _ => NextSoundMode()));
    }

    // ---- master ----

    /// <summary>Hotkey and wheel steps run off the UI thread (Windows audio calls are slow); the pop-up shows from the remembered level at once.</summary>
    private void StepMaster(int delta) => Task.Run(() =>
    {
        if (_steps.Step(MixerService.MasterKey, delta, ReadMaster, level => WriteMaster(level, delta), ShowMaster) is { } level) ShowMaster(level);
    });

    private static int? ReadMaster() => CoreAudio.GetMaster(out var level, out _) ? level : null;

    private void WriteMaster(int level, int delta)
    {
        if (delta > 0 && CoreAudio.GetMaster(out _, out var muted) && muted) CoreAudio.SetMasterMute(false);
        _osd.NoteOwnChange(OsdService.VolumeElement);
        CoreAudio.SetMasterVolume(level);
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
        if (!CoreAudio.GetMaster(out var v, out var muted)) return;
        _osd.ShowVolume(v, !muted);
        _osd.NoteOwnChange(OsdService.VolumeElement);
        CoreAudio.SetMasterMute(!muted);
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
        if (!ShowsMixer) return;
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
