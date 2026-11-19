using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// One band of a graphic equaliser as the editor shows it: its fixed frequency, its gain and the slider position that drives it. The
/// slider always runs over the same positions (<see cref="MinPos"/>..<see cref="MaxPos"/>); what one position is worth follows the
/// equaliser's range (±12 or ±24 dB), so switching the range never makes a slider clip a gain. Shown as a vertical slider in the
/// editor and as a card (<c>GraphicBandRow</c>) in the plain Parameters list.
public sealed partial class GraphicBandItem : ObservableObject
{
    /// The slider's positions each side of 0 dB: one position is a tenth of a dB at ±12 (a fifth at ±24).
    private const double PosSpan = 120;
    public const double MinPos = -PosSpan, MaxPos = PosSpan;

    /// Where a double-click (and Home or 0 on the keyboard) puts the slider: 0 dB.
    public const double ResetPos = 0;

    /// Up/Down (and the Parameters slider's Left/Right) move this many positions: 0.5 dB at ±12; Shift and PageUp/PageDown 3 dB.
    public const double SmallChange = 5, LargeChange = 30;

    // Typed limits for the Parameters list's value box: the whole range a band can hold, whichever range the sliders show.
    public static double MinGain => Eq.MinGain;
    public static double MaxGain => Eq.MaxGain;

    private readonly Func<double> _rangeDb;
    private readonly Action<GraphicBandItem> _edited;
    private double _gain;
    private bool _silent;

    /// <param name="rangeDb">The equaliser's current range: the gain at either end of the slider.</param>
    /// <param name="edited">Told after the user changed the gain.</param>
    public GraphicBandItem(double freq, Func<double> rangeDb, Action<GraphicBandItem> edited)
    {
        Freq = freq;
        _rangeDb = rangeDb;
        _edited = edited;
    }

    public double Freq { get; }

    /// Under the slider: "31.5", "1.25k".
    public string ShortLabel => DrawKit.ShortFreq(Freq);

    /// The Parameters card's name and the readout: "31.5 Hz", "1.25 kHz".
    public string FreqText => DrawKit.Freq(Freq);

    public double Gain
    {
        get => _gain;
        set
        {
            value = Math.Clamp(Math.Round(value, 1), Eq.MinGain, Eq.MaxGain);
            if (_gain == value) return;
            _gain = value;
            Changed();
        }
    }

    public string GainText => $"{DrawKit.SignedDb(Gain)} dB";

    /// The slider's view of the gain (held at the slider's ends when the gain is beyond the range). Set only when the slider really moved, so a typed value keeps its precision.
    public double GainPos
    {
        get => Math.Round(Math.Clamp(Gain / _rangeDb() * PosSpan, MinPos, MaxPos));
        set { if (Math.Round(value) != GainPos) Gain = value / PosSpan * _rangeDb(); }
    }

    /// The range changed: the slider shows the same gain at its new place.
    public void RangeChanged() => OnPropertyChanged(nameof(GainPos));

    private void Changed()
    {
        OnPropertyChanged(nameof(Gain));
        OnPropertyChanged(nameof(GainPos));
        OnPropertyChanged(nameof(GainText));
        if (!_silent) _edited(this);
    }

    /// Takes a saved gain without counting as an edit.
    public void Load(double gain)
    {
        _silent = true;
        Gain = gain;
        _silent = false;
    }
}
