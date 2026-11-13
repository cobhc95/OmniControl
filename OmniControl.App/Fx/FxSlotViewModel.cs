using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio;
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
    private int _number;

    public FxSlotViewModel(FxSlot slot, Action save)
    {
        _slot = slot;
        _save = save;
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
        FxEngines.None => "Saved only: this effect doesn't process sound yet",
        FxEngines.Program => "Runs in OmniControl's engine (not in Equalizer APO, which runs the master and microphones in Fast mode)",
        FxEngines.Apo => "Runs only in Equalizer APO (the master and microphones in Fast mode)",
        _ when IsPlugin => "Runs in both sound modes; in Equalizer APO with the settings saved here",
        _ => "Runs in both sound modes",
    };

    /// <summary>The detail behind <see cref="RunsNote"/> (its tooltip) for a plug-in Equalizer APO also runs; null otherwise.</summary>
    public string? RunsDetail => IsPlugin && FxCatalog.EnginesOf(_slot).HasFlag(FxEngines.Apo)
        ? "In Fast mode Equalizer APO runs this plug-in for the master and microphones inside Windows' audio engine, without its window, with the settings saved in OmniControl: changes made in its window reach Windows when the window closes. Only at a wet mix of 100 %. A plug-in that misbehaves there can interrupt Windows audio."
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

        var loading = false;
        ChoiceItem preset = null!;
        preset = new ChoiceItem("Preset", layout.PresetNames, layout.PresetNameFor(bands), name =>
        {
            if (loading || name == EqLayout.CustomName) return;
            loading = true;
            editor.Load(layout.FromPreset(name));
            _slot.Bands = editor.Export();
            loading = false;
            _save();
        });
        editor.Edited += () =>
        {
            _slot.Bands = editor.Export();
            loading = true;
            preset.Selected = layout.PresetNameFor(_slot.Bands);
            loading = false;
            _save();
        };
        _presetChoice = preset;
        _eqSettings = BuildParams(() => editor.OverallGainDb = FxCatalog.EqGainOf(_slot));
        return _eqSettings.Prepend(editor).ToList();
    }

    /// <param name="changed">Called after a parameter changed (the equaliser redraws its curve with the overall gain).</param>
    private List<object> BuildParams(Action? changed = null)
    {
        var kind = FxCatalog.Find(_slot.Kind);
        while (_slot.Values.Count < kind.Params.Count) _slot.Values.Add(kind.Params[_slot.Values.Count].Default);
        return kind.Params.Select((p, i) => (object)new SliderItem(p.Name, (int)Math.Round(_slot.Values[i]),
            v => { _slot.Values[i] = v; _save(); changed?.Invoke(); return OperationResult.Ok("FX"); }, _ => { },
            (int)p.Min, (int)p.Max, p.Unit, (int)Math.Round(p.Default))).ToList();
    }
}
