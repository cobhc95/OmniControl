using System.Runtime.InteropServices;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// One display-only loopback of a playback device, started in the background and released by <see cref="Dispose"/> (also while it is
/// still starting). The block callback runs on the capture's own thread and may only draw from the sound; a failure is reported once, on
/// the UI thread, and the hold is then spent. UI thread to make and dispose.
internal sealed class DisplayHold : IDisposable
{
    private readonly object _gate = new();
    private readonly Action<string> _failed;
    private readonly Action<bool> _fedChanged;
    private readonly CoreAudio.MasterWatcher _level = new();
    private DisplayTap? _tap;
    private bool _disposed;
    private volatile float _gain = 1f; // 0 = the display is not fed (muted or too quiet to level-match)

    public string DeviceId { get; }

    /// The loopback comes after the device's own master volume, but Equalizer APO's real copy runs before it: every block is scaled back
    /// by the amplitude that volume left (<see cref="CoreAudio.LoopbackLevel"/>: its dB as a linear factor, 1 when the device turns the
    /// volume down itself), so the display sees the track as at 100 %. Nothing else is applied (no mono sum, no pan law).
    /// <param name="failed">Called on the UI thread with a plain sentence when the sound can't be heard (Windows refused, the device went away).</param>
    /// <param name="fedChanged">Called on the UI thread with false while the display is not fed (device muted or nearly silent) and true when it is fed again.</param>
    public DisplayHold(string deviceId, DisplayBlock onBlock, Action<string> failed, Action<bool> fedChanged)
    {
        DeviceId = deviceId;
        _failed = failed;
        _fedChanged = fedChanged;
        _level.Device = () => DeviceId;
        _level.Changed += OnLevel;
        _level.Start();
        _ = Guard.RunAsync("display tap", () => StartAsync(onBlock));
    }

    /// The watcher's thread: the device's level or mute changed. The gain is read from Windows on a worker, never inside its notification.
    private void OnLevel(int percent, bool muted)
    {
        if (muted || percent < Timing.DisplayFeedMinPercent) SetGain(0f);
        else ThreadPool.QueueUserWorkItem(_ => Guard.Run("display tap level", () => SetGain(CoreAudio.LoopbackLevel(DeviceId) is float level and > 0f ? 1f / level : 100f / percent)));
    }

    private void SetGain(float gain)
    {
        var wasFed = _gain > 0f;
        _gain = gain;
        if (wasFed != gain > 0f) Dispatcher.UIThread.Post(() => { if (!_disposed) _fedChanged(gain > 0f); });
    }

    private async Task StartAsync(DisplayBlock onBlock)
    {
        DisplayTap tap;
        try
        {
            // the watcher only reports changes, so the level at the start is read once
            if (CoreAudio.GetMaster(out var percent, out var muted, DeviceId)) OnLevel(percent, muted);
            tap = await DisplayTap.StartAsync(DeviceId, block =>
            {
                var gain = _gain;
                if (gain == 0f) return; // never feed silence as if it were the real sound
                if (gain != 1f) for (var i = 0; i < block.Length; i++) block[i] *= gain;
                onBlock(block);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            // Windows refused to record this device: the display just stays empty and says why.
            Fail(ex.Message);
            return;
        }
        lock (_gate)
        {
            if (_disposed)
            {
                tap.Dispose();
                return;
            }
            _tap = tap;
        }
        tap.Failed += Fail;
    }

    private void Fail(string reason) => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed) _failed(reason);
    });

    public void Dispose()
    {
        _level.Changed -= OnLevel;
        _level.Dispose();
        DisplayTap? tap;
        lock (_gate)
        {
            _disposed = true;
            tap = _tap;
            _tap = null;
        }
        if (tap is not null) tap.Failed -= Fail;
        tap?.Dispose();
    }
}
