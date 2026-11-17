using Avalonia.Threading;
using OmniControl.App.Audio;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.App.States;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

// "Mute the way your keyboard does" for the microphone mute: when the user has taught OmniControl their keyboard's mute key (MicMuteKey), the
// mute sends that key, then the real mute is read again a moment later; if the key changed nothing, Windows' own mute is used. While that
// second read waits, the microphone state says it is being read (its liveness), which the tray tile shows as its small grey dot.
public sealed partial class VolumePageViewModel
{
    private const string MicKeyOptionKey = "keymute";

    private readonly MicMuteKey _micKey;
    private int _micChecking;
    private bool _fallbackLogged;

    /// <summary>The setting row of the microphone mute action (its live settings pane).</summary>
    private ActionEditor MicMuteEditor()
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

    /// <summary>What the microphone state tells the tray tile: nothing is being changed, and it is being read while the key's result is checked.</summary>
    private StateLiveness MicLiveness() => new(false, Volatile.Read(ref _micChecking) > 0, null, null);

    /// <summary>Sends the learned mute key for the default microphone when one is in use; false means the normal mute must be used. Runs inside <see cref="StepInput"/>'s lock.</summary>
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
        _states.NotifyLiveness(MicMutedId);
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
                Dispatcher.UIThread.Post(() =>
                {
                    if (_inputs.TryGetValue(deviceId, out var item)) item.Sync(level, muted, item.FxOn, item.FxCount);
                    _states.Notify(MicMutedId);
                });
            }
        }
        finally
        {
            Interlocked.Decrement(ref _micChecking);
            _states.NotifyLiveness(MicMutedId);
        }
    }
}
