using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>
/// One band of the equaliser as the editor shows it: values, the slider positions that drive them (frequency and bandwidth on a log
/// scale, gain in half-dB steps) and their texts. Setting a value tells the panel, which redraws and saves.
/// </summary>
public sealed partial class EqBandItem : ObservableObject
{
    /// <summary>The Type drop-down, in the order it lists them (lows, bell, highs, then the special types).</summary>
    private static readonly IReadOnlyList<(EqType Type, string Name)> Types = new[]
    {
        (EqType.LowShelf, "Low shelf"), (EqType.HighShelf, "High shelf"), (EqType.Peak, "Band"),
        (EqType.LowPass, "Low pass"), (EqType.HighPass, "High pass"), (EqType.AllPass, "All pass"),
        (EqType.Notch, "Notch"), (EqType.BandPass, "Band pass"),
    };

    public static IReadOnlyList<string> TypeNames { get; } = Types.Select(t => t.Name).ToList();

    /// <summary>The frequency and bandwidth sliders run from 0 to this (log scale); one step is about 0.05 octave of frequency.</summary>
    public const double LogSteps = 200;

    /// <summary>The gain slider moves in half-dB steps.</summary>
    private const double GainStepsPerDb = 2;
    public static double MinGainSteps => Eq.MinGain * GainStepsPerDb;
    public static double MaxGainSteps => Eq.MaxGain * GainStepsPerDb;

    // Typed limits for the value boxes (NumberText).
    public static double MinFreq => Eq.MinFreq;
    public static double MaxFreq => Eq.MaxFreq;
    public static double MinGain => Eq.MinGain;
    public static double MaxGain => Eq.MaxGain;
    public static double MinQ => Eq.MinQ;
    public static double MaxQ => Eq.MaxQ;
    public static double MinBandwidth => Eq.MinBandwidth;
    public static double MaxBandwidth => Eq.MaxBandwidth;

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    /// <summary>A4 = 440 Hz is MIDI note 69.</summary>
    private const double A4Freq = 440, A4Note = 69;

    private readonly Action<EqBandItem> _edited;
    private readonly Action<EqBandItem> _select;
    private double _freq, _gain, _q = Eq.DefaultQ;
    private EqType _type;
    private bool _enabled = true;
    private bool _silent;

    public EqBandItem(int index, Action<EqBandItem> edited, Action<EqBandItem> select)
    {
        _index = index;
        _edited = edited;
        _select = select;
    }

    /// <summary>The band's identity in the engine (kept when other bands are added or removed).</summary>
    public int Id { get; private set; }

    /// <summary>The band's place (0-based); changes when a band before it is removed.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Number), nameof(Title), nameof(DefaultFreqPos), nameof(DefaultGainSteps), nameof(DefaultWidthPos))]
    private int _index;

    public string Number => (Index + 1).ToString();
    public string Title => $"Band {Number}";
    [ObservableProperty] private bool _isSelected;

    public bool Enabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; Changed(); } }
    public double Freq { get => _freq; set => Set(ref _freq, Math.Clamp(value, Eq.MinFreq, Eq.MaxFreq)); }
    public double Gain { get => _gain; set => Set(ref _gain, Math.Clamp(Math.Round(value, 1), Eq.MinGain, Eq.MaxGain)); }
    public double Q { get => _q; set => Set(ref _q, Math.Clamp(value, Eq.MinQ, Eq.MaxQ)); }
    public EqType Type { get => _type; set { if (_type == value) return; _type = value; Changed(); } }

    /// <summary>The width in octaves: the same setting as <see cref="Q"/>, converted by <see cref="EqMath.BandwidthOf"/>.</summary>
    public double Bandwidth
    {
        get => EqMath.BandwidthOf(Q);
        set => Q = EqMath.QOf(Math.Clamp(value, Eq.MinBandwidth, Eq.MaxBandwidth));
    }

    /// <summary>False for types that ignore gain (pass, notch, band-pass, all-pass): the gain control is greyed out.</summary>
    public bool UsesGain => EqMath.UsesGain(Type);

    // Slider views of the values. A setter changes the value only when the slider really moved, so a typed value keeps its precision.
    public double FreqPos
    {
        get => ToLogPos(Freq, Eq.MinFreq, Eq.MaxFreq);
        set { if (Math.Round(value) != FreqPos) Freq = FromLogPos(value, Eq.MinFreq, Eq.MaxFreq); }
    }
    public double GainSteps
    {
        get => ToGainSteps(Gain);
        set { if (Math.Round(value) != GainSteps) Gain = value / GainStepsPerDb; }
    }
    public double WidthPos
    {
        get => ToLogPos(Bandwidth, Eq.MinBandwidth, Eq.MaxBandwidth);
        set { if (Math.Round(value) != WidthPos) Bandwidth = FromLogPos(value, Eq.MinBandwidth, Eq.MaxBandwidth); }
    }

    // Where a double-click puts each slider: this place's default (Eq.DefaultFor).
    public double DefaultFreqPos => ToLogPos(Eq.DefaultFor(Index).Freq, Eq.MinFreq, Eq.MaxFreq);
    public double DefaultGainSteps => ToGainSteps(Eq.DefaultFor(Index).Gain);
    public double DefaultWidthPos => ToLogPos(EqMath.BandwidthOf(Eq.DefaultFor(Index).Q), Eq.MinBandwidth, Eq.MaxBandwidth);

    /// <summary>The mouse wheel over this band's point on the graph: wider (up) or narrower, as far as the wheel moves the slider.</summary>
    public void NudgeWidth(int direction) => WidthPos = Math.Clamp(WidthPos + direction * ValueWheel.StepOf(0, LogSteps), 0, LogSteps);

    private static double ToLogPos(double value, double min, double max) => Math.Round(LogSteps * Math.Log(value / min) / Math.Log(max / min));
    private static double FromLogPos(double pos, double min, double max) => min * Math.Pow(max / min, pos / LogSteps);
    private static double ToGainSteps(double gain) => Math.Round(gain * GainStepsPerDb);

    public string TypeName
    {
        get => Types.First(t => t.Type == Type).Name;
        set { foreach (var t in Types) if (t.Name == value) Type = t.Type; }
    }

    public string FreqText => DrawKit.Freq(Freq);
    public string GainTextDb => $"{GainText} dB";
    private string GainText => DrawKit.SignedDb(Gain);
    public string BandwidthText => $"{Bandwidth:0.00} oct";
    public string QText => $"Q {Q:0.00}";

    /// <summary>The nearest note and how far off it is ("A4", "C#3 +12 ct").</summary>
    public string NoteText
    {
        get
        {
            var note = A4Note + 12 * Math.Log2(Freq / A4Freq);
            var nearest = (int)Math.Round(note);
            var cents = (int)Math.Round((note - nearest) * 100);
            var name = $"{NoteNames[((nearest % 12) + 12) % 12]}{nearest / 12 - 1}";
            return cents == 0 ? name : $"{name} {cents:+0;-0} ct";
        }
    }

    /// <summary>The frequency as the band list shows it ("80", "2.5k").</summary>
    public string ListFreq => DrawKit.ShortFreq(Freq);

    public bool IsBypassed => !Enabled;

    [RelayCommand] private void Select() => _select(this);

    /// <summary>Turns the band on or bypasses it (double-click on its circle, Space on its row).</summary>
    public void ToggleEnabled() => Enabled = !Enabled;

    private void Set(ref double field, double value)
    {
        if (field == value) return;
        field = value;
        Changed();
    }

    private static readonly string[] Shown =
    {
        nameof(Enabled), nameof(Freq), nameof(Gain), nameof(Q), nameof(Bandwidth), nameof(Type), nameof(UsesGain), nameof(FreqPos),
        nameof(GainSteps), nameof(WidthPos), nameof(TypeName), nameof(FreqText), nameof(GainTextDb), nameof(BandwidthText),
        nameof(QText), nameof(NoteText), nameof(ListFreq), nameof(IsBypassed),
    };

    private void Changed()
    {
        foreach (var n in Shown) OnPropertyChanged(n);
        if (!_silent) _edited(this);
    }

    /// <summary>Takes values from saved settings without counting as an edit.</summary>
    public void Load(EqBand band)
    {
        _silent = true;
        Id = band.Id;
        _enabled = band.Enabled; _freq = band.Freq; _gain = band.Gain; _q = band.Q; _type = band.Type;
        Changed();
        _silent = false;
    }

    public EqBand ToBand() => new() { Id = Id, Enabled = Enabled, Freq = Freq, Gain = Gain, Q = Q, Type = Type };
}
