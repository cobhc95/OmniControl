using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio;
using OmniControl.App.Pages;
using OmniControl.Core.Models;

namespace OmniControl.App.Fx;

/// <summary>One effect of a chain as the FX window edits it: on/off, wet mix, and the editor cards for its parameters.</summary>
public sealed class FxSlotViewModel : ObservableObject
{
    private readonly FxSlot _slot;
    private readonly Action _save;

    public FxSlotViewModel(FxSlot slot, Action save)
    {
        _slot = slot;
        _save = save;
        EditorItems = slot.Kind switch
        {
            "eq" => BuildEq(),
            FxCatalog.PluginKind => Array.Empty<object>(),
            FxCatalog.IrKind => BuildParams().Prepend(new IrFileItem(slot.Path, p => { slot.Path = p; save(); })).ToList(),
            _ => BuildParams(),
        };
    }

    public FxSlot Slot => _slot;
    public string Name => _slot.Name;
    public bool IsPlugin => _slot.Kind == FxCatalog.PluginKind;
    public string Tag => IsPlugin ? "VST3" : "FX";

    /// <summary>Shown in place of the editor when the effect has nothing to edit here.</summary>
    public string Hint => IsPlugin
        ? "This plug-in is kept in the chain, but running VST3 plug-ins isn't built yet, so it doesn't change the sound or open its own window."
        : string.Empty;

    public bool Enabled
    {
        get => _slot.Enabled;
        set { if (_slot.Enabled == value) return; _slot.Enabled = value; OnPropertyChanged(); _save(); }
    }

    public int Wet
    {
        get => _slot.Wet;
        set { value = Math.Clamp(value, 0, 100); if (_slot.Wet == value) return; _slot.Wet = value; OnPropertyChanged(); _save(); }
    }

    /// <summary>The cards shown on the right when this effect is selected (sliders, or the equaliser graph).</summary>
    public IReadOnlyList<object> EditorItems { get; }

    /// <summary>The preset picker shown in the toolbar (the equaliser only; null for effects without presets).</summary>
    public ChoiceItem? PresetChoice { get; private set; }
    public bool HasPresets => PresetChoice is not null;

    private IReadOnlyList<object> BuildEq()
    {
        var bands = _slot.Bands ??= Eq.Flat();
        var panel = new EqPanelItem();
        panel.Load(bands);

        var loading = false;
        ChoiceItem preset = null!;
        preset = new ChoiceItem("Preset", Eq.Presets.Select(p => p.Name).Append("Custom").ToList(), Eq.PresetNameFor(bands), name =>
        {
            if (loading || name == "Custom") return;
            loading = true;
            panel.Load(Eq.FromPreset(name));
            _slot.Bands = panel.Export();
            loading = false;
            _save();
        });
        panel.Edited += () =>
        {
            _slot.Bands = panel.Export();
            loading = true;
            preset.Selected = Eq.PresetNameFor(_slot.Bands);
            loading = false;
            _save();
        };
        PresetChoice = preset;
        return new object[] { panel };
    }

    private List<object> BuildParams()
    {
        var kind = FxCatalog.Find(_slot.Kind);
        while (_slot.Values.Count < kind.Params.Count) _slot.Values.Add(kind.Params[_slot.Values.Count].Default);
        return kind.Params.Select((p, i) => (object)new SliderItem(p.Name, (int)Math.Round(_slot.Values[i]),
            v => { _slot.Values[i] = v; _save(); return OperationResult.Ok("FX"); }, _ => { },
            (int)p.Min, (int)p.Max, p.Unit)).ToList();
    }
}
