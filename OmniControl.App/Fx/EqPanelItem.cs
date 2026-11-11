using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>One band of the equaliser as the page edits it. Setting a value tells the panel, which redraws and saves.</summary>
public sealed partial class EqBandItem : ObservableObject
{
    public static readonly IReadOnlyList<string> TypeNames = new[] { "Low shelf", "Peak", "High shelf" };

    private readonly Action<EqBandItem> _edited;
    private readonly Action<EqBandItem> _select;
    private double _freq, _gain, _q;
    private EqType _type;
    private bool _silent;

    public EqBandItem(int index, Action<EqBandItem> edited, Action<EqBandItem> select)
    {
        Index = index;
        _edited = edited;
        _select = select;
    }

    public int Index { get; }
    public string Number => (Index + 1).ToString();
    [ObservableProperty] private bool _isSelected;

    public double Freq { get => _freq; set => Set(ref _freq, Math.Clamp(value, Eq.MinFreq, Eq.MaxFreq)); }
    public double Gain { get => _gain; set => Set(ref _gain, Math.Clamp(Math.Round(value, 1), Eq.MinGain, Eq.MaxGain)); }
    public double Q { get => _q; set => Set(ref _q, Math.Clamp(value, Eq.MinQ, Eq.MaxQ)); }
    public EqType Type { get => _type; set { if (_type == value) return; _type = value; Changed(); } }

    // Slider views of the values (frequency and Q move on a log scale, gain in half-dB steps).
    public double FreqPos
    {
        get => Math.Round(1000 * Math.Log(Freq / Eq.MinFreq) / Math.Log(Eq.MaxFreq / Eq.MinFreq));
        set { if (Math.Round(value) != FreqPos) Freq = Eq.MinFreq * Math.Pow(Eq.MaxFreq / Eq.MinFreq, value / 1000); }
    }
    public double GainSteps
    {
        get => Math.Round(Gain * 2);
        set { if (Math.Round(value) != GainSteps) Gain = value / 2; }
    }
    public double QPos
    {
        get => Math.Round(1000 * Math.Log(Q / Eq.MinQ) / Math.Log(Eq.MaxQ / Eq.MinQ));
        set { if (Math.Round(value) != QPos) Q = Eq.MinQ * Math.Pow(Eq.MaxQ / Eq.MinQ, value / 1000); }
    }
    public string TypeName
    {
        get => TypeNames[(int)Type];
        set { var i = TypeNames.ToList().IndexOf(value); if (i >= 0) Type = (EqType)i; }
    }

    public string FreqText => Freq >= 1000 ? $"{Freq / 1000:0.##} kHz" : $"{Freq:0} Hz";
    public string GainText => Gain > 0 ? $"+{Gain:0.0}" : $"{Gain:0.0}";
    public string GainTextDb => $"{GainText} dB";
    public string QText => Q.ToString("0.00");

    [RelayCommand] private void Select() => _select(this);

    private void Set(ref double field, double value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        Changed();
    }

    private void Changed()
    {
        foreach (var n in new[] { nameof(Freq), nameof(Gain), nameof(Q), nameof(Type), nameof(FreqPos), nameof(GainSteps), nameof(QPos),
                     nameof(TypeName), nameof(FreqText), nameof(GainText), nameof(GainTextDb), nameof(QText) })
            OnPropertyChanged(n);
        if (!_silent) _edited(this);
    }

    /// <summary>Takes values from saved settings without counting as an edit.</summary>
    public void Load(EqBand band)
    {
        _silent = true;
        _freq = band.Freq; _gain = band.Gain; _q = band.Q; _type = band.Type;
        Changed();
        _silent = false;
    }

    public EqBand ToBand() => new() { Freq = Freq, Gain = Gain, Q = Q, Type = Type };
}

/// <summary>The graph, the band strip and the selected band's controls: one card in the Equaliser group.</summary>
public sealed partial class EqPanelItem : ObservableObject, IEditorItem, ISideNavigable
{
    /// <summary>Left/Right on the card pick the previous or next band.</summary>
    public void Step(int direction) => SelectedIndex = Math.Clamp(SelectedIndex + direction, 0, Eq.Bands - 1);

    private bool _loading;

    public EqPanelItem()
    {
        Bands = Enumerable.Range(0, Eq.Bands).Select(i => new EqBandItem(i, OnEdited, b => SelectedIndex = b.Index)).ToArray();
        Load(Eq.Flat());
        SelectedIndex = 3;
    }

    public string Label => "Equaliser graph";
    public EqBandItem[] Bands { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Selected))] private int _selectedIndex = -1;
    public EqBandItem Selected => Bands[Math.Clamp(SelectedIndex, 0, Eq.Bands - 1)];

    /// <summary>A band was changed by the user: save it.</summary>
    public event Action? Edited;

    /// <summary>Anything the graph shows changed.</summary>
    public event Action? Redraw;

    partial void OnSelectedIndexChanged(int value)
    {
        foreach (var b in Bands) b.IsSelected = b.Index == value;
        Redraw?.Invoke();
    }

    private void OnEdited(EqBandItem band)
    {
        if (!_loading) Edited?.Invoke();
        Redraw?.Invoke();
    }

    public void Load(IReadOnlyList<EqBand> bands)
    {
        _loading = true;
        for (var i = 0; i < Eq.Bands; i++) Bands[i].Load(i < bands.Count ? bands[i] : Eq.DefaultBand(i));
        _loading = false;
        Redraw?.Invoke();
    }

    public List<EqBand> Export() => Bands.Select(b => b.ToBand()).ToList();

    /// <summary>Puts the selected band back to its default frequency, gain, Q and type.</summary>
    public void ResetSelected()
    {
        var d = Eq.DefaultBand(Selected.Index);
        var b = Selected;
        b.Type = d.Type; b.Freq = d.Freq; b.Q = d.Q; b.Gain = d.Gain;
    }
}
