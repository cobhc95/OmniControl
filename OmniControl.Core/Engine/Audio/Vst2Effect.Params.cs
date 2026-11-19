namespace OmniControl.Core.Engine;

/// A VST2 plug-in's settings as a host's own parameter list sees them (ported from TabForge's <c>Vst2Plugin.Parameters</c>): every
/// parameter by its index with the plug-in's name, its own display text and label, and a 0..1 value. VST2 has no steps, units of
/// its own, groups or defaults: the list is continuous, a typed number is a percent of the range, and the default is the value the
/// plug-in had when it was loaded. Changes are made under the audio lock, so they never run beside the plug-in's processing.
internal sealed unsafe partial class Vst2Effect
{
    /// More parameters than this are not listed or saved (the plug-in's count is not trusted blindly).
    private const int MaxParameters = 65536;

    /// A stored value further from 0..1 than this is skipped when loading (some plug-ins keep values past 0..1; absurd ones are damage).
    private const float MaxStoredValue = 1e6f;

    /// Every parameter's value right after loading, before saved settings were given back: the plug-in's own defaults.
    private readonly float[] _defaults = Array.Empty<float>();

    private IReadOnlyList<PluginParam>? _parameters;

    private int ParameterCount => _disposed || _effect->getParameter == IntPtr.Zero ? 0 : Math.Clamp(_effect->numParams, 0, MaxParameters);

    public IReadOnlyList<PluginParam> Parameters => _parameters ??= ReadParameters();

    private IReadOnlyList<PluginParam> ReadParameters()
    {
        var list = new List<PluginParam>(ParameterCount);
        for (var i = 0; i < ParameterCount; i++)
            list.Add(new PluginParam((uint)i, NameOf(i), string.Empty, 0, i < _defaults.Length ? Math.Clamp(_defaults[i], 0, 1) : 0, string.Empty));
        return list;
    }

    private string NameOf(int index) => AskText(Vst2Abi.EffGetParamName, index) is { Length: > 0 } name ? name : $"Parameter {index + 1}";

    public double ValueOf(uint id) => id < ParameterCount ? Math.Clamp(Finite(GetValue((int)id)), 0, 1) : 0;

    public void Set(uint id, double value)
    {
        if (id >= ParameterCount || !double.IsFinite(value)) return;
        lock (_processGate)
        {
            if (!_disposed) SetValue((int)id, (float)Math.Clamp(value, 0, 1));
        }
    }

    /// VST2 can only describe the current value: its display text with its label ("-6.0 dB"); <paramref name="value"/> is the one just set.
    public string TextOf(uint id, double value)
    {
        if (id >= ParameterCount) return string.Empty;
        var shown = AskText(Vst2Abi.EffGetParamDisplay, (int)id);
        var label = AskText(Vst2Abi.EffGetParamLabel, (int)id);
        return label.Length > 0 && shown.Length > 0 && !shown.EndsWith(label, StringComparison.Ordinal) ? $"{shown} {label}" : shown;
    }

    /// VST2 has no values in units of their own: typing uses the percent of the range (0-100).
    public double PlainOf(uint id) => ValueOf(id) * Models.Percent.Max;

    public void SetPlain(uint id, double plain) => Set(id, plain / Models.Percent.Max);

    private float GetValue(int index) => ((delegate* unmanaged[Cdecl]<Vst2Abi.AEffect*, int, float>)_effect->getParameter)(_effect, index);

    private void SetValue(int index, float value)
    {
        if (_effect->setParameter == IntPtr.Zero || !float.IsFinite(value) || MathF.Abs(value) > MaxStoredValue) return;
        ((delegate* unmanaged[Cdecl]<Vst2Abi.AEffect*, int, float, void>)_effect->setParameter)(_effect, index, value);
    }

    private static double Finite(float value) => float.IsFinite(value) ? value : 0;

    /// Every parameter's value as the plug-in has it now.
    private float[] ReadValues()
    {
        var values = new float[ParameterCount];
        for (var i = 0; i < values.Length; i++) values[i] = GetValue(i);
        return values;
    }

    /// The values with the parameters' names, as they are saved (Equalizer APO finds them by name).
    private IReadOnlyList<(string Name, float Value)> Named(float[] values)
    {
        var named = new (string, float)[values.Length];
        for (var i = 0; i < values.Length; i++) named[i] = (AskText(Vst2Abi.EffGetParamName, i), values[i]);
        return named;
    }
}
