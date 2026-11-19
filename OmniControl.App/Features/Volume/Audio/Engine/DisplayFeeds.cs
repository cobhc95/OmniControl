using OmniControl.App.Features.Volume.Audio.Engine.Dsp;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// Everything that feeds a display copy with what you hear (a display-only loopback of the playing device, see <see cref="DisplayTap"/>),
/// for the effects Equalizer APO runs inside Windows where OmniControl never sees their sound: a plug-in's editor copy (so its meters
/// and analysers move) while its editor is open, and the Parametric equaliser graph's spectrum (<see cref="OpenSpectrum"/>). The feed is
/// for the display only: the plug-in's output is thrown away, nothing is played or routed, and nothing here counts as processing.
/// Effects OmniControl's own engine runs get the real sound and no feed. <see cref="IsFed"/> says, for the FX window's tag, which
/// effects are fed this way now. UI thread.
public sealed class DisplayFeeds : IDisposable
{
    private readonly PluginChainHost _plugins;
    private readonly EffectsEngine _engine;
    private readonly Action<string> _report;
    private readonly Dictionary<FxSlot, DisplayHold> _feeds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FxSlot, int> _heardSpectra = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<FxSlot> _paused = new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public DisplayFeeds(PluginChainHost plugins, EffectsEngine engine, Action<string> report)
    {
        _plugins = plugins;
        _engine = engine;
        _report = report;
        plugins.EditorWindowChanged += Sync;
        engine.StatusChanged += OnEngineStatus;
    }

    /// A feed started or ended (UI thread): the FX window's tag follows it.
    public event Action? Changed;

    /// The effect's display is fed with what you hear now.
    public bool IsFed(FxSlot slot) => _feeds.ContainsKey(slot) || _heardSpectra.ContainsKey(slot);

    /// The effect has a feed, but the device is muted or nearly silent, so nothing is fed now (the display holds its last picture).
    public bool IsPaused(FxSlot slot) => _paused.Contains(slot);

    /// The live spectrum of the equaliser built from <paramref name="slot"/> in the row <paramref name="key"/> (null = the master) until the result is disposed.
    public IDisposable OpenSpectrum(string? key, FxSlot slot, Action<float[]?> onFrame) =>
        new SpectrumSession(_engine, key, slot, onFrame, heard => MarkHeard(slot, heard), fed => MarkFed(slot, fed), _report);

    private void MarkFed(FxSlot slot, bool fed)
    {
        if (fed ? !_paused.Remove(slot) : !_paused.Add(slot)) return;
        Changed?.Invoke();
    }

    private void MarkHeard(FxSlot slot, bool heard)
    {
        var count = _heardSpectra.GetValueOrDefault(slot) + (heard ? 1 : -1);
        if (count > 0) _heardSpectra[slot] = count;
        else _heardSpectra.Remove(slot);
        Changed?.Invoke();
    }

    private void OnEngineStatus(string? key)
    {
        // the method or the device may have changed: every feed follows or ends, and an open editor whose effect Equalizer APO now runs gets one
        foreach (var slot in _feeds.Keys.Concat(_plugins.EditorSlots()).Distinct().ToList()) Sync(slot);
    }

    /// Brings the plug-in's feed in line: on while its editor is open and Equalizer APO runs it, off otherwise.
    private void Sync(FxSlot slot)
    {
        if (_disposed) return;
        var device = _plugins.HasEditorWindow(slot) ? _engine.DisplayDeviceOf(null, slot) : null;
        if (_feeds.TryGetValue(slot, out var running))
        {
            if (running.DeviceId == device) return;
            running.Dispose(); // stopped before the plug-in can be unloaded
            _feeds.Remove(slot);
            _paused.Remove(slot);
        }
        if (device is not null && _plugins.Get(slot).Effect is { } effect)
        {
            var stage = new PluginStage(effect);
            _feeds[slot] = new DisplayHold(device, block => stage.Process(block, 2), reason =>
            {
                Stop(slot);
                _report($"Couldn't feed {slot.Name}'s display: {reason}");
            }, fed => MarkFed(slot, fed));
        }
        Changed?.Invoke();
    }

    private void Stop(FxSlot slot)
    {
        if (!_feeds.Remove(slot, out var hold)) return;
        hold.Dispose();
        _paused.Remove(slot);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        _plugins.EditorWindowChanged -= Sync;
        _engine.StatusChanged -= OnEngineStatus;
        foreach (var hold in _feeds.Values) hold.Dispose();
        _feeds.Clear();
    }
}
