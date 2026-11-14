using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Pages;
using OmniControl.Core.Models;

namespace OmniControl.App.Fx;

/// <summary>
/// One effect of a chain as the FX window edits it: its place in the chain, on/off, wet mix, and the editor cards for its parameters.
/// How its editor is shown (scale, the plain settings list) is in FxSlotViewModel.View.cs.
/// </summary>
public sealed partial class FxSlotViewModel : ObservableObject
{
    private readonly FxSlot _slot;
    private readonly Action _save;
    private readonly Func<FxSlot, DynamicsReading?> _readings;
    private readonly IPresetHost _presetHost;
    private PresetPicker? _presets;
    private int _number;

    /// <param name="readings">What the effects engine measures for a dynamics effect of this chain now (null: nothing), for the compressor's and the limiter's meters.</param>
    /// <param name="presets">Where the preset pull-down finds the user's presets and opens the manager.</param>
    internal FxSlotViewModel(FxSlot slot, Action save, Func<FxSlot, DynamicsReading?> readings, IPresetHost presets)
    {
        _presetHost = presets;
        _slot = slot;
        _save = save;
        _readings = readings;
    }

    public FxSlot Slot => _slot;
    public string Name => _slot.Name;
    public bool IsPlugin => FxCatalog.IsPlugin(_slot);

    /// <summary>The short kind shown on the row before the name: the plug-in's format ("VST3", "VST2") or "FX" for a built-in effect.</summary>
    public string Tag => FxCatalog.PluginTypeOf(_slot)?.Tag ?? BuiltInTag;

    private const string BuiltInTag = "FX";

    /// <summary>What kind of effect it is, said on the row's tooltip.</summary>
    public string KindText => FxCatalog.PluginTypeOf(_slot)?.Description ?? "Built-in effect";

    /// <summary>Its place in the chain, from 1 at the top: the sound passes the effects in this order.</summary>
    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, value);
    }

    /// <summary>Which engines change the sound with this effect (from the catalog's <see cref="FxKind.Engines"/>), said plainly.</summary>
    public string RunsNote => FxCatalog.EnginesOf(_slot) switch
    {
        FxEngines.None => "Saved only: not processed yet",
        FxEngines.Program => "Runs in OmniControl's engine only",
        FxEngines.Apo => "Runs in Equalizer APO only",
        _ when IsPlugin => "Runs in both sound modes",
        _ => "Runs in both sound modes",
    };

    /// <summary>The detail behind <see cref="RunsNote"/> (its tooltip) for a plug-in Equalizer APO also runs; null otherwise.</summary>
    public string? RunsDetail => IsPlugin && FxCatalog.EnginesOf(_slot).HasFlag(FxEngines.Apo)
        ? "Fast mode: Equalizer APO runs it inside Windows, without its window, with the settings saved here; changes reach Windows when its window closes. Only at a wet mix of 100 %."
        : null;

    public bool Enabled
    {
        get => _slot.Enabled;
        set { if (_slot.Enabled == value) return; _slot.Enabled = value; OnPropertyChanged(); _save(); }
    }

    public int Wet
    {
        get => _slot.Wet;
        set { value = Percent.Clamp(value); if (_slot.Wet == value) return; _slot.Wet = value; OnPropertyChanged(); _save(); }
    }

    /// <summary>Where a double-click on the Wet slider puts it.</summary>
    public double DefaultWet => FxSlot.DefaultWet;

    /// <summary>The cards shown on the right when this effect is selected (sliders, or the equaliser graph; none for a plug-in, whose own editor shows).</summary>
    /// <remarks>Built the first time the effect is selected, not for every effect of the chain when the window opens (an equaliser's graph and band cards are the costly ones).</remarks>
    public IReadOnlyList<object> EditorItems => _editorItems ??= BuildEditor();

    private IReadOnlyList<object>? _editorItems;
    private ChoiceItem? _presetChoice;

    /// <summary>The preset picker shown in the toolbar (the equaliser only; null for effects without presets).</summary>
    public ChoiceItem? PresetChoice
    {
        get { _ = EditorItems; return _presetChoice; }
    }

    public bool HasPresets => PresetChoice is not null;

    private IReadOnlyList<object> BuildEditor()
    {
        if (FxCatalog.EqLayoutOf(_slot) is { } eq) return BuildEq(eq);
        if (IsPlugin) return Array.Empty<object>();
        return _slot.Kind switch
        {
            FxCatalog.IrKind => BuildParams().Prepend(new IrFileItem(_slot.Path, p => { _slot.Path = p; _save(); })).ToList(),
            FxCatalog.CompKind or FxCatalog.LimiterKind => BuildDynamics(),
            _ => BuildParams(),
        };
    }

    /// <summary>Any equaliser: its editor card (a graphic one's sliders, or the Parametric's graph), the shared preset choice, then the overall gain card.</summary>
    private IReadOnlyList<object> BuildEq(EqLayout layout)
    {
        var bands = _slot.Bands ??= layout.Default();
        IEqEditor editor = layout.Graphic is { } fixedBands
            ? new GraphicEqItem(fixedBands, Pref.WideRange, wide => SavePref(p => p.WideRange = wide))
            : new EqPanelItem();
        _eqEditor = editor;
        editor.Load(bands);
        editor.OverallGainDb = FxCatalog.EqGainOf(_slot);

        _presets = new PresetPicker(new PresetSource(FxCatalog.KindOf(_slot)!, () => new PresetContent { Bands = editor.Export() },
            content => { editor.Load(content.Bands!); _slot.Bands = editor.Export(); }, () => _slot.Custom, custom => _slot.Custom = custom),
            _presetHost, _save);
        editor.Edited += () =>
        {
            _slot.Bands = editor.Export();
            _presets.Edited();
            _save();
        };
        _presetChoice = _presets.Choice;
        _eqSettings = BuildParams(() => editor.OverallGainDb = FxCatalog.EqGainOf(_slot));
        // The Parametric editor carries the overall gain as its own vertical slider; the graphic ones keep the card under them.
        if (editor is EqPanelItem panel)
        {
            panel.OverallGain = _eqSettings.OfType<SliderItem>().FirstOrDefault();
            return new List<object> { editor };
        }
        return _eqSettings.Prepend(editor).ToList();
    }

    /// <summary>Lets go of what the preset pull-down subscribed to (the effect leaves the chain, or the window closes).</summary>
    public void ReleasePresets() => _presets?.Dispose();

    /// <summary>One card per setting of the effect's catalog entry, in its order: a slider, or a switch for an on/off setting. Also kept in <see cref="_paramCards"/> so a preset can show its values.</summary>
    /// <param name="changed">Called after a parameter changed (the equaliser and the dynamics cards redraw their curves).</param>
    private List<object> BuildParams(Action? changed = null)
    {
        FxCatalog.Complete(_slot);
        var kind = FxCatalog.Find(_slot.Kind);
        _paramCards = kind.Params.Select((p, i) => p.IsSwitch
            ? (object)new ToggleItem(p.Name, CompressorParams.IsOn(_slot.Values[i]), on => { _slot.Values[i] = on ? 1 : 0; _save(); changed?.Invoke(); }) { Half = true }
            : new SliderItem(p.Name, (int)Math.Round(_slot.Values[i]),
                v => { _slot.Values[i] = v; _save(); changed?.Invoke(); return OperationResult.Ok("FX"); }, _ => { },
                (int)p.Min, (int)p.Max, p.Unit, (int)Math.Round(p.Default)) { Thick = true }).ToList();
        return _paramCards;
    }

    private List<object> _paramCards = new();
}
