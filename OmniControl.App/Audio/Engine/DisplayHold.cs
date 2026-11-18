using System.Runtime.InteropServices;
using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// One display-only loopback of a playback device, started in the background and released by <see cref="Dispose"/> (also while it is
/// still starting). The block callback runs on the capture's own thread and may only draw from the sound; a failure is reported once, on
/// the UI thread, and the hold is then spent. UI thread to make and dispose.
/// </summary>
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

    /// <summary>
    /// The loopback comes after the device's master volume, but Equalizer APO's real copy runs before it: every block is scaled by
    /// 1 / the master volume so the display sees the full-volume signal.
    /// </summary>
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

    private void OnLevel(int percent, bool muted)
    {
        var wasFed = _gain > 0f;
        _gain = muted || percent < Timing.DisplayFeedMinPercent ? 0f : 100f / percent;
        if (wasFed != _gain > 0f) Dispatcher.UIThread.Post(() => { if (!_disposed) _fedChanged(_gain > 0f); });
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
