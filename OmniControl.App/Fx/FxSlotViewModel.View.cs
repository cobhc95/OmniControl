using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Services;

namespace OmniControl.App.Fx;

/// <summary>
/// How the effect's editor is shown, remembered per effect type in settings.json (<see cref="AppSettings.EffectEditors"/>; a built-in
/// kind, or a plug-in's file, so every copy of the same plug-in looks the same on a given screen): its scale (<see cref="EditorScale"/>)
/// and whether the plain settings list shows instead of the editor (<see cref="ShowsParameters"/>): the equaliser's bands as rows of
/// sliders, or a plug-in's own settings (<see cref="PluginParameters"/>, set by whoever opened the plug-in for it).
/// </summary>
public sealed partial class FxSlotViewModel
{
    /// <summary>Plug-ins (every format) are stored under their file, after this prefix (a built-in kind never has it; kept from when only VST3 existed).</summary>
    private const string PluginPrefKey = "vst3:";

    private IEqEditor? _eqEditor;
    private List<object>? _eqSettings;
    private ObservableCollection<object>? _eqParameters;
    private PluginParameterList? _pluginParameters;
    private bool _scaleRefused;

    private string PrefKey => IsPlugin ? PluginPrefKey + _slot.Path.ToLowerInvariant() : _slot.Kind;

    private EffectEditorPref Pref => AppSettings.Current.EffectEditors.TryGetValue(PrefKey, out var pref) ? pref : new EffectEditorPref();

    private void SavePref(Action<EffectEditorPref> change)
    {
        var all = AppSettings.Current.EffectEditors;
        if (!all.TryGetValue(PrefKey, out var pref)) all[PrefKey] = pref = new EffectEditorPref();
        change(pref);
        AppSettings.Current.Save();
    }

    // ---- the plain settings list ----

    /// <summary>The effect has an editor besides its settings (the equaliser's graph, a plug-in's own window); the others already are a list of sliders.</summary>
    public bool CanShowParameters => IsPlugin || FxCatalog.EqLayoutOf(_slot) is not null;

    public string ParametersTip => CanShowParameters
        ? "Shows the settings as a plain list of sliders, for small screens (press again for the editor)"
        : "This effect's editor already is a plain list of its settings";

    /// <summary>The plain settings list shows instead of the editor (the user pressed Parameters; off at first).</summary>
    public bool ShowsParameters
    {
        get => CanShowParameters && Pref.Parameters;
        set
        {
            if (!CanShowParameters || value == ShowsParameters) return;
            SavePref(p => p.Parameters = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShownItems));
            OnScaleTargetChanged();
        }
    }

    [RelayCommand] private void ToggleParameters() => ShowsParameters = !ShowsParameters;

    /// <summary>A plug-in's settings list while it is open for this effect (null otherwise).</summary>
    public PluginParameterList? PluginParameters
    {
        get => _pluginParameters;
        set
        {
            if (ReferenceEquals(_pluginParameters, value)) return;
            if (_pluginParameters is not null) _pluginParameters.PropertyChanged -= OnPluginParametersChanged;
            _pluginParameters = value;
            if (value is not null) value.PropertyChanged += OnPluginParametersChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShownItems));
        }
    }

    private void OnPluginParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginParameterList.Groups)) OnPropertyChanged(nameof(ShownItems));
    }

    /// <summary>The cards OmniControl draws for this effect now: its editor cards, or its settings as a plain list.</summary>
    public IReadOnlyList<object>? ShownItems => !ShowsParameters ? EditorItems : IsPlugin ? PluginParameters?.Groups : EqParameters;

    /// <summary>
    /// An equaliser's settings list: one card per band (the Parametric's on, type, frequency, gain, bandwidth; a graphic one's gain),
    /// then the overall gain; follows the bands.
    /// </summary>
    private IReadOnlyList<object>? EqParameters
    {
        get
        {
            _ = EditorItems;
            if (_eqParameters is null && _eqEditor is { } editor)
            {
                _eqParameters = new ObservableCollection<object>();
                if (editor.BandCards is INotifyCollectionChanged changing) changing.CollectionChanged += OnEqBandsChanged;
                FillEqParameters();
            }
            return _eqParameters;
        }
    }

    private void OnEqBandsChanged(object? sender, NotifyCollectionChangedEventArgs e) => FillEqParameters();

    private void FillEqParameters()
    {
        if (_eqParameters is null || _eqEditor is null) return;
        _eqParameters.Clear();
        foreach (var band in _eqEditor.BandCards) _eqParameters.Add(band);
        foreach (var setting in _eqSettings ?? new List<object>()) _eqParameters.Add(setting);
    }

    // ---- scale ----

    private int ScalePercent => EditorScale.Valid(Pref.Scale);

    /// <summary>The Scale drop-down's entry (one of <see cref="EditorScale.Texts"/>).</summary>
    public string ScaleText
    {
        get => EditorScale.TextOf(ScalePercent);
        set
        {
            var percent = EditorScale.Parse(value);
            if (percent == ScalePercent) return;
            SavePref(p => p.Scale = percent);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScaleFactor));
        }
    }

    /// <summary>Ctrl+- / Ctrl+= (and the FX menu): the next smaller or larger scale on the list; Ctrl+0: 100 %.</summary>
    [RelayCommand] private void ScaleDown() => StepScale(-1);
    [RelayCommand] private void ScaleUp() => StepScale(1);
    [RelayCommand] private void ScaleReset() { if (CanScale) ScaleText = EditorScale.TextOf(EditorScale.Default); }

    private void StepScale(int direction)
    {
        if (!CanScale) return;
        var at = Math.Clamp(EditorScale.Percents.ToList().IndexOf(ScalePercent) + direction, 0, EditorScale.Percents.Count - 1);
        ScaleText = EditorScale.Texts[at];
    }

    /// <summary>The scale as a factor (0.7), for OmniControl's editors and a plug-in's own window.</summary>
    public double ScaleFactor => EditorScale.Factor(ScalePercent);

    /// <summary>False while the scale would go to a plug-in's own window that did not change its size when asked.</summary>
    public bool CanScale => !(IsPlugin && !ShowsParameters && _scaleRefused);

    public string ScaleTip => CanScale
        ? "Scale: makes the whole editor smaller or larger (Ctrl+- / Ctrl+=, Ctrl+0 for 100 %)"
        : "This plug-in did not change its window's size when asked, so OmniControl can't scale it. Its own settings may offer a size; Parameters shows a list that scales.";

    /// <summary>The plug-in's own window ignored the scale (see <c>IPluginEditor.TryScale</c>): it stays at 100 % and the drop-down is greyed out.</summary>
    public void PluginScaleRefused()
    {
        _scaleRefused = true;
        ScaleText = EditorScale.TextOf(EditorScale.Default);
        OnScaleTargetChanged();
    }

    private void OnScaleTargetChanged()
    {
        OnPropertyChanged(nameof(CanScale));
        OnPropertyChanged(nameof(ScaleTip));
    }
}
