using Avalonia.Threading;
using OmniControl.App.Audio.Engine.Dsp;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// The live spectrum of one equaliser graph, for as long as the graph is on screen. Every <see cref="Timing.SpectrumFrame"/> it asks the
/// engine where the sound is: in OmniControl's own engine the effect's own tap (the real signal entering it); for an effect Equalizer
/// APO runs, a display-only loopback of the device (what you hear, see <see cref="DisplayHold"/>). The FFT runs on the thread pool and
/// the picture is handed to <c>onFrame</c> on the UI thread (null while there is no sound source). Dispose stops everything and releases
/// the capture. UI thread.
/// </summary>
internal sealed class SpectrumSession : IDisposable
{
    private readonly EffectsEngine _engine;
    private readonly string? _key;
    private readonly FxSlot _slot;
    private readonly Action<float[]?> _onFrame;
    private readonly Action<bool> _heardChanged, _fedChanged;
    private readonly Action<string> _report;
    private readonly SpectrumAnalyzer _analyzer = new();
    private readonly DispatcherTimer _timer = new() { Interval = Timing.SpectrumFrame };
    private DisplayHold? _hold;
    private SpectrumTap? _heardTap, _ownTap;
    private string? _device;
    private bool _busy, _disposed, _hadPicture;

    /// <param name="heardChanged">True while the spectrum is fed with what you hear (the honest label), false when it has the real signal or none.</param>
    public SpectrumSession(EffectsEngine engine, string? key, FxSlot slot, Action<float[]?> onFrame, Action<bool> heardChanged, Action<bool> fedChanged, Action<string> report)
    {
        _fedChanged = fedChanged;
        _engine = engine;
        _key = key;
        _slot = slot;
        _onFrame = onFrame;
        _heardChanged = heardChanged;
        _report = report;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void Tick()
    {
        if (_disposed) return;
        var device = _engine.DisplayDeviceOf(_key, _slot);
        if (device != _device) Listen(device);
        var tap = device is null ? _engine.OwnTapOf(_key, _slot) : _heardTap;
        if (!ReferenceEquals(tap, _ownTap) && device is null)
        {
            if (_ownTap is not null) _ownTap.Enabled = false; // the stage that was replaced
            _ownTap = tap;
        }
        if (tap is null)
        {
            if (_hadPicture) _onFrame(null);
            _hadPicture = false;
            return;
        }
        tap.Enabled = true;
        if (_busy) return;
        _ = Guard.RunAsync("spectrum", () => AnalyseAsync(tap));
    }

    /// <summary>Starts, changes or ends the loopback of the device Equalizer APO plays on.</summary>
    private void Listen(string? device)
    {
        _hold?.Dispose();
        _hold = null;
        _heardTap = null;
        _device = device;
        if (device is not null)
        {
            var tap = _heardTap = new SpectrumTap { Enabled = true };
            _hold = new DisplayHold(device, block => tap.Write(block, 2), reason => _report($"Couldn't hear the sound for the spectrum: {reason}"), _fedChanged);
        }
        _fedChanged(true);
        _heardChanged(device is not null);
    }

    private async Task AnalyseAsync(SpectrumTap tap)
    {
        _busy = true;
        try
        {
            var frame = await Task.Run(() => _analyzer.Compute(tap));
            if (_disposed || frame is null) return;
            _hadPicture = true;
            _onFrame(frame);
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _hold?.Dispose();
        _hold = null;
        if (_ownTap is not null) _ownTap.Enabled = false;
        if (_device is not null)
        {
            _fedChanged(true);
            _heardChanged(false);
        }
    }
}
