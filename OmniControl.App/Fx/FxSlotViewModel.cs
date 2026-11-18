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
public sealed partial class FxSlotViewModel : ObservableObject, IFxPresetTarget
{
    private readonly FxSlot _slot;
    private readonly Action _save;
    private readonly Func<FxSlot, DynamicsReading?> _readings;
    private readonly Func<FxSlot, Action<float[]?>, IDisposable> _spectrum;
    private readonly IPresetHost _presetHost;
    private PresetPicker? _presets;
    private int _number;

    /// <param name="readings">What the effects engine measures for a dynamics effect of this chain now (null: nothing), for the compressor's and the limiter's meters.</param>
    /// <param name="presets">Where the preset pull-down finds the user's presets and opens the manager.</param>
    internal FxSlotViewModel(FxSlot slot, Action save, Func<FxSlot, DynamicsReading?> readings, Func<FxSlot, Action<float[]?>, IDisposable> spectrum, IPresetHost presets)
    {
        _spectrum = spectrum;
        _presetHost = presets;
        _slot = slot;
        _save = save;
        _readings = readings;
    }

    public FxSlot Slot => _slot;
    public string Name => _slot.Name;
    public bool IsPlugin => FxCatalog.IsPlugin(_slot);

    /// <summary>The slot is a device folder (<see cref="FxFolders"/>); <see cref="Folder"/> is what its row and editor show.</summary>
    public bool IsFolder => _slot.IsFolder;
    public bool IsEffect => !_slot.IsFolder;
    public FxFolderViewModel? Folder { get; internal set; }

    private bool _isChild, _showsAllDevices, _shown = true;

    /// <summary>The row is in the list: false while its folder is closed.</summary>
    public bool Shown
    {
        get => _shown;
        internal set => SetProperty(ref _shown, value);
    }

    /// <summary>A short muted line of what the effect is set to (its first settings), for its folder's list.</summary>
    public string Summary => FxCatalog.PluginTypeOf(_slot)?.Tag
        ?? (FxCatalog.KindOf(_slot) is not { } kind ? string.Empty
            : kind.Eq is not null ? $"{_slot.Bands?.Count ?? 0} bands"
            : string.Join(" · ", kind.Params.Select((p, i) => (p, i)).Where(x => x.i < _slot.Values.Count && !x.p.IsSwitch).Take(2)
                .Select(x => $"{x.p.Name} {Math.Round(_slot.Values[x.i], 1)}{x.p.Unit}")));

    internal void NotifySummary() => OnPropertyChanged(nameof(Summary));

    /// <summary>The effect is inside a folder (its row is indented under an accent bar).</summary>
    public bool IsChild
    {
        get => _isChild;
        internal set => SetProperty(ref _isChild, value);
    }

    /// <summary>The effect is outside every folder while folders exist: it runs for every device, said at the right of its row.</summary>
    public bool ShowsAllDevices
    {
        get => _showsAllDevices;
        internal set => SetProperty(ref _showsAllDevices, value);
    }

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
        _ => "Runs in OmniControl's engine and in Equalizer APO",
    };

    /// <summary>The detail behind <see cref="RunsNote"/> (its tooltip) for a plug-in Equalizer APO also runs; null otherwise.</summary>
    public string? RunsDetail => IsPlugin && FxCatalog.EnginesOf(_slot).HasFlag(FxEngines.Apo)
        ? "In Equalizer APO it runs inside Windows, without its window, with the settings saved here; changes reach Windows when its window closes. Only at a wet mix of 100 %."
        : _slot.Kind == FxCatalog.IrKind
            ? "In Equalizer APO the file, Gain and Auto level apply (Auto level as a Preamp worked out from the WAV file); Trim silence does not, and neither does a Mix below 100 %."
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
        if (_slot.IsFolder) return Array.Empty<object>();
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
            panel.OpenSpectrum = onFrame => _spectrum(_slot, onFrame);
            panel.OverallGain = _eqSettings.OfType<SliderItem>().FirstOrDefault();
            panel.ApplyOverallGain = db =>
            {
                _slot.Values[0] = db;
                _save();
                panel.OverallGainDb = db;
                panel.OverallGain?.Load((int)Math.Round(db));
            };
            return new List<object> { editor };
        }
        return _eqSettings.Prepend(editor).ToList();
    }

    /// <summary>Lets go of what the preset pull-down subscribed to (the effect leaves the chain, or the window closes).</summary>
    public void ReleasePresets() => _presets?.Dispose();

    // The preset manager reads and applies through the effect's own preset box, so the editor on screen shows the change.
    PresetContent IFxPresetTarget.Read()
    {
        _ = EditorItems; // the preset box is made with the editor
        return _presets?.Read() ?? PresetCatalog.Read(FxCatalog.Find(_slot.Kind), _slot);
    }

    void IFxPresetTarget.Apply(PresetContent content)
    {
        _ = EditorItems;
        _presets?.Apply(content);
    }

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
