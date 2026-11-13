using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.Core.Engine;

namespace OmniControl.App.Fx;

/// <summary>
/// One setting of a plug-in in OmniControl's plain settings list: its name, a slider over its range (whole steps for a stepped
/// setting, <see cref="ContinuousSteps"/> for a continuous one), the plug-in's own text for the value, and the value in its own units
/// for typing. Changes go to the plug-in at once (see <see cref="IPluginEffect.Set"/>); the list re-reads every setting after one,
/// because a plug-in may move others with it. Once the list's plug-in is closed (<c>isOpen</c> false) nothing touches it.
/// </summary>
public sealed class PluginParamItem : ObservableObject
{
    /// <summary>Slider steps of a continuous setting (0.1 % each; the wheel moves 1 %).</summary>
    private const int ContinuousSteps = 1000;

    /// <summary>A typed value is handed to the plug-in, which holds it within the setting's range; these only keep the box sane.</summary>
    public const double TypedMin = -1e9, TypedMax = 1e9;

    /// <summary>Decimals kept when a value is typed in the setting's own units.</summary>
    public const int TypedDecimals = 2;

    private readonly IPluginEffect _effect;
    private readonly PluginParam _param;
    private readonly Action _changed;
    private readonly Func<bool> _isOpen;
    private double _position;

    /// <param name="changed">Called after the user changed this setting.</param>
    /// <param name="isOpen">False once the plug-in is closed for the list.</param>
    public PluginParamItem(IPluginEffect effect, PluginParam param, Action changed, Func<bool> isOpen)
    {
        _effect = effect;
        _param = param;
        _changed = changed;
        _isOpen = isOpen;
        Maximum = param.StepCount > 0 ? param.StepCount : ContinuousSteps;
        ResetPosition = PositionOf(param.DefaultValue);
        _position = PositionOf(effect.ValueOf(param.Id));
    }

    public string Title => _param.Title;
    public string Group => _param.Group;
    public int Maximum { get; }

    /// <summary>Where a double-click on the slider puts it: the plug-in's own default.</summary>
    public double ResetPosition { get; }

    /// <summary>The slider's place (0..<see cref="Maximum"/>).</summary>
    public double Position
    {
        get => _position;
        set
        {
            value = Math.Clamp(Math.Round(value), 0, Maximum);
            if (value == _position || !_isOpen()) return;
            _position = value;
            _effect.Set(_param.Id, value / Maximum);
            _changed();
        }
    }

    /// <summary>The value in the setting's own units, typed with a double-click on the value text.</summary>
    public double Plain
    {
        get => _isOpen() ? _effect.PlainOf(_param.Id) : 0;
        set
        {
            if (!_isOpen()) return;
            _effect.SetPlain(_param.Id, value);
            _changed();
        }
    }

    /// <summary>The plug-in's own words for the value, with its unit ("8.4 kHz", "Lowpass").</summary>
    public string ValueText
    {
        get
        {
            var text = _isOpen() ? _effect.TextOf(_param.Id, _position / Maximum) : string.Empty;
            if (text.Length == 0) text = $"{_position * Core.Models.Percent.Max / Maximum:0.#} %";
            return _param.Units.Length == 0 || text.EndsWith(_param.Units, StringComparison.Ordinal) ? text : $"{text} {_param.Units}";
        }
    }

    /// <summary>The plug-in may have changed this setting itself (it follows another): read it again.</summary>
    public void Refresh()
    {
        var now = PositionOf(_effect.ValueOf(_param.Id));
        if (now != _position)
        {
            _position = now;
            OnPropertyChanged(nameof(Position));
        }
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(Plain));
    }

    /// <summary>True when the words match the setting's name or its group (search box).</summary>
    public bool Matches(string words) => Services.TextMatch.Any(words, Title, Group);

    private double PositionOf(double normalized) => Math.Round(Math.Clamp(normalized, 0, 1) * Maximum);
}
