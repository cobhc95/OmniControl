using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>
/// A graphic equaliser's editor (32 or 16 fixed bands, <see cref="GraphicBands"/>): one vertical slider per band, the total response
/// drawn above them, Flat and the ±12 / ±24 dB range. One card in the FX window's editor list; keys are the shared vertical-slider
/// keys of <see cref="RowEditing"/>.
/// </summary>
public sealed partial class GraphicEqItem : ObservableObject, IEditorItem, IEqEditor, IRedrawSource
{
    /// <summary>The editor's keys, listed in the FX window's Keyboard shortcuts (handled by the shared vertical-slider keys in RowEditing).</summary>
    public const string KeyHelp = "Left/Right move between bands; Up/Down change a band (Shift or PageUp/PageDown: large steps, Ctrl: fine); Home or 0 sets it to 0 dB; Esc, then Up/Down, goes to the next card. "
        + "With the mouse: drag across the sliders to draw a curve; double-click a slider for 0 dB; Parameters lets you type values.";

    /// <summary>An equaliser with this many bands or fewer gets the large sliders (the 16-band one: easiest to operate).</summary>
    private const int LargeUpTo = 16;

    /// <summary>The two ranges the sliders can reach (dB either side of 0).</summary>
    private const double NarrowRangeDb = 12, WideRangeDb = Eq.MaxGain;

    private readonly GraphicBands _set;
    private readonly Action<bool> _saveWide;
    private bool _wide;

    /// <param name="wideRange">The sliders start at ±24 dB instead of ±12 (the user's choice for this kind).</param>
    /// <param name="saveWide">Keeps a new range choice.</param>
    public GraphicEqItem(GraphicBands set, bool wideRange, Action<bool> saveWide)
    {
        _set = set;
        _wide = wideRange;
        _saveWide = saveWide;
        var large = set.Centres.Count <= LargeUpTo;
        Bands = set.Centres.Select(freq => new GraphicBandItem(freq, large, () => RangeDb, OnBandEdited)).ToList();
    }

    public string Label => "Equaliser bands";

    public IReadOnlyList<GraphicBandItem> Bands { get; }
    IReadOnlyList<object> IEqEditor.BandCards => Bands;

    /// <summary>The sliders reach ±24 dB (on) or ±12 dB (off). Only the sliders' reach changes; every gain stays as it is.</summary>
    public bool WideRange
    {
        get => _wide;
        set
        {
            if (_wide == value) return;
            _wide = value;
            _saveWide(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RangeDb));
            foreach (var band in Bands) band.RangeChanged();
            Redraw?.Invoke();
        }
    }

    /// <summary>The gain at either end of the sliders, and of the curve and scale drawn beside them.</summary>
    public double RangeDb => WideRange ? WideRangeDb : NarrowRangeDb;

    /// <summary>The band last changed or given the keyboard: the curve reads out its frequency and gain.</summary>
    public GraphicBandItem? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) Redraw?.Invoke(); }
    }
    private GraphicBandItem? _selected;

    public double OverallGainDb
    {
        get => _overallGainDb;
        set { _overallGainDb = value; Redraw?.Invoke(); }
    }
    private double _overallGainDb;

    public event Action? Edited;

    /// <summary>Anything the curve shows changed.</summary>
    public event Action? Redraw;

    /// <summary>Every band back to 0 dB (one edit).</summary>
    [RelayCommand]
    private void Flat()
    {
        Load(_set.Flat());
        Edited?.Invoke();
    }

    private void OnBandEdited(GraphicBandItem band)
    {
        Selected = band;
        Edited?.Invoke();
        Redraw?.Invoke();
    }

    /// <summary>Shows saved bands (in the set's order, as <see cref="GraphicBands.Upgrade"/> and the presets make them) without counting as an edit.</summary>
    public void Load(IReadOnlyList<EqBand> bands)
    {
        for (var i = 0; i < Bands.Count; i++) Bands[i].Load(i < bands.Count ? bands[i].Gain : 0);
        Redraw?.Invoke();
    }

    public List<EqBand> Export() => _set.Bands(Bands.Select(b => b.Gain).ToList());
}
