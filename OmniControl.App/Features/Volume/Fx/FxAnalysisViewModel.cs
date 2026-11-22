using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Fx;

/// The FX window toolbar's analysis of one chain: its total response as measured through the whole chain (<see cref="ShowMeasured"/>;
/// until the first measurement, and while effects are off, the EQ and gain curve of <see cref="ChainResponse"/> worked out on every
/// edit through <see cref="Show"/>, which stays as the larger graph's reference), the scale it is drawn at, its peak, the
/// measurement's facts as tags, and the live Latency of the row (read every
/// <see cref="Timing.FxAnalysisRefresh"/> through the reader it is given). Every number says only what is really known: "-" while
/// nothing runs, "none added" when Equalizer APO processes the row. The larger graph's open and pinned state
/// is in FxAnalysisViewModel.Panel.cs. Dispose when the window closes.
public sealed partial class FxAnalysisViewModel : ObservableObject, IRedrawSource, IDisposable
{
    /// The scale the graphs start at (dB above and below 0); a Peak above it is shown in the caution colour.
    public const double DefaultScaleDb = 12;

    /// A curve beyond the default scale widens it in these steps to its extreme plus a margin (a +24 dB band: +-27), at most <see cref="MaxScaleDb"/>.
    private const double ScaleStepDb = 3, ScaleMarginDb = 1.5, MaxScaleDb = 30;

    /// A peak up to this is no boost (float noise around 0 dB).
    private const double BoostFromDb = 0.05;

    private const int CurvePoints = 400;

    /// Where the total response is worked out and drawn: 20 Hz to 20 kHz, evenly on a log scale.
    public static readonly double[] Freqs = EqMath.LogGrid(CurvePoints, Eq.MinFreq, Eq.MaxFreq);

    public const string NoteText = "Shows EQ and gain only";

    /// The larger graph's words for the faint reference curve behind a measured one.
    public const string ReferenceText = "- - - EQ and gain only";

    /// A ring-out shorter than this is a filter's, not a reverb's or delay's: no tag.
    private const double TailFromSeconds = 0.1;

    private readonly Func<RowLoad> _readLoad;
    private readonly DispatcherTimer _refresh = new() { Interval = Timing.FxAnalysisRefresh };
    private ChainCurve _curve = new(new double[Freqs.Length], 0, Eq.MinFreq, Array.Empty<string>(), false);
    private FxEngines _engine = FxEngines.Program;
    private RowLoad _load;
    private ChainMeasurement? _measured;

    /// <param name="readLoad">Where the row's effects run now and their delay (<see cref="EffectsEngine.LoadOf"/>).</param>
    public FxAnalysisViewModel(Func<RowLoad> readLoad)
    {
        _readLoad = readLoad;
        _refresh.Tick += OnRefresh;
        _refresh.Start();
        _openTimer.Tick += OnOpenTimer;
        _closeTimer.Tick += OnCloseTimer;
        RefreshLoad();
    }

    public event Action? Redraw;

    /// The total response at each of <see cref="Freqs"/>, in dB: the measured one when there is one.
    public IReadOnlyList<double> Curve => _measured?.Db ?? _curve.Db;

    /// The EQ and gain curve the larger graph draws faintly behind a measured one.
    public IReadOnlyList<double> Reference => _curve.Db;

    /// <see cref="Curve"/> is a measurement of the whole chain.
    public bool IsMeasured => _measured is not null;

    /// A measurement runs now; the last curve stays until it ends.
    public bool IsMeasuring { get; private set; }

    /// The measurement's facts as tags (none before the first measurement).
    public IReadOnlyList<TagItem> Notes { get; private set; } = Array.Empty<TagItem>();

    /// The row's effects are switched on (off: the curve is what the chain would do, drawn muted, and nothing is measured).
    public bool IsOn { get; private set; }

    /// How far above and below 0 dB the graphs reach: <see cref="DefaultScaleDb"/>, or wider to fit the whole curve.
    public double ScaleDb { get; private set; } = DefaultScaleDb;

    public string ScaleText => $"±{ScaleDb:0}";

    public double PeakDb => _measured?.PeakDb ?? _curve.PeakDb;
    public double PeakHz => _measured?.PeakHz ?? _curve.PeakHz;
    public bool HasBoost => IsOn && PeakDb > BoostFromDb;
    public bool PeakIsHigh => IsOn && PeakDb > DefaultScaleDb;

    public string PeakText => IsOn ? DrawKit.ReadoutDb(PeakDb) : "-";
    public string PeakAtText => IsOn ? $"at {DrawKit.ReadoutFreq(PeakHz)}" : string.Empty;

    public string PeakTip =>
        !IsOn ? "Effects are off: the graph shows what the chain would do."
        : HasBoost ? $"Boost of {DrawKit.ReadoutDb(PeakDb)} at about {DrawKit.ReadoutFreq(PeakHz)}. {EngineStatus.HeadroomNote(_engine)}"
        : "No boost: the curve stays at or below 0 dB.";

    public string LatencyText => _measured is { } m ? m.LatencyMs is { } ms ? $"{ms} ms" : "not known" : _load.Place switch
    {
        LoadPlace.Own => $"{_load.DelayMs} ms",
        LoadPlace.Apo => _curve.RunsPlugins ? "not known" : "none added",
        _ => "-",
    };

    public string LatencyTip => _measured is { } m ? MeasuredLatencyTip(m) : _load.Place switch
    {
        LoadPlace.Own => $"About {_load.DelayMs} ms (estimate): recording and playing {_load.DelayMs - _load.PluginDelayMs} ms"
                         + (_load.PluginDelayMs > 0 ? $", plug-ins {_load.PluginDelayMs} ms." : ", no plug-in delay."),
        LoadPlace.Apo => "Equalizer APO adds no delay of its own"
                         + (_curve.RunsPlugins ? "; its plug-ins may add some." : "."),
        _ => NotRunningTip,
    };

    private string MeasuredLatencyTip(ChainMeasurement m)
    {
        var effects = m.LatencyMs is { } ms ? $"The effects delay the sound by {ms} ms (measured)." : "The effects' delay did not show clearly in the measurement.";
        return _load.Place switch
        {
            LoadPlace.Own => $"{effects} Recording and playing add about {_load.DelayMs - _load.PluginDelayMs} ms.",
            LoadPlace.Apo => $"{effects} Equalizer APO adds no delay of its own.",
            _ => effects,
        };
    }

    private string NotRunningTip => IsOn ? "Not running now (see the status line)." : "Effects are off.";

    public bool HasNote => _measured is null && _curve.NotDrawn.Count > 0;
    public string NoteTip => "Not in the curve: " + string.Join(", ", _curve.NotDrawn) + ".";

    /// The chain, the engine that runs the row (<see cref="ModeMethod.Engine"/>) or the row's on/off changed: work the curve out again.
    /// <param name="apoBlends">Equalizer APO blends a wet mix below 100 % (<see cref="EffectsRules.ApoBlends"/>).</param>
    public void Show(IReadOnlyList<FxSlot> chain, FxEngines engine, bool apoBlends, bool on)
    {
        _engine = engine;
        IsOn = on;
        _curve = ChainResponse.Of(chain, engine, apoBlends, Freqs);
        if (!on) SetMeasured(null);
        Changed();
    }

    /// A measurement started (the last curve stays until <see cref="ShowMeasured"/>), or none is needed after all.
    public void SetMeasuring(bool measuring)
    {
        IsMeasuring = measuring;
        OnPropertyChanged(nameof(IsMeasuring));
    }

    /// A measurement of the whole chain ended; null = nothing to measure or it failed (back to the EQ and gain curve).
    public void ShowMeasured(ChainMeasurement? measured)
    {
        IsMeasuring = false;
        SetMeasured(IsOn ? measured : null);
        Changed();
    }

    private void SetMeasured(ChainMeasurement? measured)
    {
        _measured = measured;
        Notes = measured is { } m ? NotesOf(m) : Array.Empty<TagItem>();
    }

    private static IReadOnlyList<TagItem> NotesOf(ChainMeasurement m)
    {
        var notes = new List<TagItem>
        {
            new("Measured: whole chain", TagTone.Neutral, m.NotMeasured.Count == 0
                ? "A short test sweep was played once through every switched-on effect, in order."
                : "A short test sweep was played once through the effects in order. Not measured: " + string.Join(", ", m.NotMeasured) + "."),
        };
        if (m.LevelDependent)
            notes.Add(new($"Distortion measured at {ChainMeasurer.TestLevelDb:0} dB", TagTone.Neutral,
                $"A compressor, limiter or plug-in may act differently at other levels: the curve is what it does at {ChainMeasurer.TestLevelDb:0} dBFS."));
        if (m.TailSeconds >= TailFromSeconds)
            notes.Add(new($"+ reverb tail {m.TailSeconds:0.0} s", TagTone.Neutral, $"The sound rings on for about {m.TailSeconds:0.0} s after it stops."));
        if (m.ApoOwnLeftOut)
            notes.Add(new("Device folder settings not included", TagTone.Neutral, EffectsRules.ApoOwnEffectsTip + " They are not in the curve."));
        return notes;
    }

    private void Changed()
    {
        ScaleDb = _measured is null ? ScaleFor(_curve.Db) : Math.Max(ScaleFor(_curve.Db), ScaleFor(_measured.Db));
        Notify(nameof(IsMeasured), nameof(IsMeasuring), nameof(Notes), nameof(IsOn), nameof(ScaleDb), nameof(ScaleText), nameof(PeakDb), nameof(PeakHz), nameof(HasBoost), nameof(PeakIsHigh),
            nameof(PeakText), nameof(PeakAtText), nameof(PeakTip), nameof(HasNote), nameof(NoteTip), nameof(LatencyText), nameof(LatencyTip));
        Redraw?.Invoke();
    }

    /// The scale that shows the whole curve: the default, or the curve's extreme plus a margin, in whole steps, held at the most.
    private static double ScaleFor(IReadOnlyList<double> curve)
    {
        var extreme = curve.Max(Math.Abs);
        if (extreme <= DefaultScaleDb) return DefaultScaleDb;
        return Math.Min(MaxScaleDb, Math.Ceiling((extreme + ScaleMarginDb) / ScaleStepDb) * ScaleStepDb);
    }

    /// The total response at <paramref name="hz"/> (between two of <see cref="Freqs"/>, read straight across on the log scale).
    public double DbAt(double hz)
    {
        var at = Math.Log(hz / Freqs[0]) / Math.Log(Freqs[^1] / Freqs[0]) * (Freqs.Length - 1);
        var i = Math.Clamp((int)Math.Floor(at), 0, Freqs.Length - 2);
        var share = Math.Clamp(at - i, 0, 1);
        return Curve[i] + (Curve[i + 1] - Curve[i]) * share;
    }

    /// The engine's status for this row changed, or the reading period ended: read where it runs and its delay.
    public void RefreshLoad()
    {
        _load = _readLoad();
        Notify(nameof(LatencyText), nameof(LatencyTip));
    }

    private void OnRefresh(object? sender, EventArgs e) => RefreshLoad();

    private void Notify(params string[] names)
    {
        foreach (var name in names) OnPropertyChanged(name);
    }

    public void Dispose()
    {
        _refresh.Stop();
        _refresh.Tick -= OnRefresh;
        _openTimer.Stop();
        _openTimer.Tick -= OnOpenTimer;
        _closeTimer.Stop();
        _closeTimer.Tick -= OnCloseTimer;
    }
}
