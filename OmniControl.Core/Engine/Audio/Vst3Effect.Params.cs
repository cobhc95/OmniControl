using NAudio.Vst3;

namespace OmniControl.Core.Engine;

/// <summary>
/// The plug-in's settings as a host's own parameter list sees them (IEditController::getParameterInfo) (UI thread only, the thread that loaded it). Changes go through
/// NAudio.Vst3's parameter path: the controller takes the value at once (IEditController::setParamNormalized) and the sound part gets
/// it in the next processed block (IParameterChanges), which is how a VST3 host must hand a change to the processor. While no chain
/// runs the plug-in, nothing processes blocks, so <see cref="FlushIfIdle"/> runs one silent frame before the settings are saved.
/// </summary>
internal sealed partial class Vst3Effect
{
    private const string GroupSeparator = " / ";

    private IReadOnlyList<PluginParam>? _parameters;
    private float[]? _flushIn, _flushOut;

    /// <summary>
    /// The settings a user may change, in the plug-in's order: read-only and hidden ones are left out, and so is the plug-in's bypass
    /// (the effect's own On switch in OmniControl does that). Read once.
    /// </summary>
    public IReadOnlyList<PluginParam> Parameters => _parameters ??= ReadParameters();

    private IReadOnlyList<PluginParam> ReadParameters() => PluginCall.Ask(() =>
    {
        var units = _plugin.Units.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First());
        return _plugin.Parameters.Where(p => !p.IsReadOnly && !p.IsHidden && !p.IsBypass)
            .Select(p => new PluginParam(p.Id, Title(p), p.Units, p.StepCount, p.DefaultNormalizedValue, GroupOf(p.UnitId, units)))
            .ToList();
    }, (IReadOnlyList<PluginParam>)Array.Empty<PluginParam>());

    private static string Title(Vst3Parameter p) => string.IsNullOrWhiteSpace(p.Title) ? p.ShortTitle : p.Title;

    /// <summary>The unit's name with the names of the units above it (the root unit, id 0, has none).</summary>
    private static string GroupOf(int unitId, IReadOnlyDictionary<int, Vst3Unit> units)
    {
        var names = new List<string>();
        var seen = new HashSet<int>();
        for (var id = unitId; id != 0 && seen.Add(id) && units.TryGetValue(id, out var unit); id = unit.ParentId)
            if (!string.IsNullOrWhiteSpace(unit.Name)) names.Insert(0, unit.Name);
        return string.Join(GroupSeparator, names);
    }

    /// <summary>The setting's current value (normalised), as the plug-in's controller has it.</summary>
    public double ValueOf(uint id) => PluginCall.Ask(() => _plugin.Parameters.TryGetById(id, out var p) ? p.NormalizedValue : 0, 0);

    /// <summary>Changes a setting (normalised); the plug-in's controller has it at once, its sound part from the next block.</summary>
    public void Set(uint id, double value) => PluginCall.Ask(() =>
    {
        if (_plugin.Parameters.TryGetById(id, out var p)) p.NormalizedValue = Math.Clamp(value, 0, 1);
        return true;
    }, false);

    /// <summary>The value as the plug-in's own editor would write it ("8.4 kHz", "Lowpass"); empty when the plug-in gives no text.</summary>
    public string TextOf(uint id, double value) => PluginCall.Ask(() => _plugin.Parameters.TryGetById(id, out var p) ? p.FormatValue(value) : string.Empty, string.Empty);

    /// <summary>The current value in the setting's own units (Hz, dB...), for typing a number.</summary>
    public double PlainOf(uint id) => PluginCall.Ask(() => _plugin.Parameters.TryGetById(id, out var p) ? p.PlainValue : 0, 0);

    /// <summary>Changes a setting by a value in its own units; the plug-in converts it, and a value past either end is held at that end.</summary>
    public void SetPlain(uint id, double plain) => PluginCall.Ask(() =>
    {
        if (_plugin.Parameters.TryGetById(id, out var p)) p.SetPlainValue(plain);
        return true;
    }, false);

    /// <summary>
    /// Hands setting changes that wait for the next block to the plug-in's sound part when no chain is running it (no block for
    /// <see cref="CoreTiming.PluginIdleMs"/>): one silent frame, processed under the same lock as the audio thread's blocks.
    /// </summary>
    private void FlushIfIdle()
    {
        if (Environment.TickCount64 - Volatile.Read(ref _lastProcessTicks) < CoreTiming.PluginIdleMs) return;
        _flushIn ??= new float[InputChannels];
        _flushOut ??= new float[OutputChannels];
        try
        {
            lock (_processGate) _plugin.Process(_flushIn, _flushOut, 1);
        }
        catch (InvalidOperationException)
        {
            // The plug-in refused the block: its settings are saved as its sound part has them, the controller's values still apply.
        }
    }
}
