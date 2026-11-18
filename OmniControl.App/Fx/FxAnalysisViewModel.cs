using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;

namespace OmniControl.App.Fx;

/// <summary>
/// The FX window toolbar's analysis of one chain: its total response (<see cref="ChainResponse"/>, recomputed on every edit through
/// <see cref="Show"/>), the scale it is drawn at, its peak, and the live Latency of the row (read every
/// <see cref="Timing.FxAnalysisRefresh"/> through the reader it is given). Every number says only what is really known: "-" while
/// nothing runs, "none added" when Equalizer APO processes the row. The larger graph's open and pinned state
/// is in FxAnalysisViewModel.Panel.cs. Dispose when the window closes.
/// </summary>
public sealed partial class FxAnalysisViewModel : ObservableObject, IRedrawSource, IDisposable
{
    /// <summary>The scale the graphs start at (dB above and below 0); a Peak above it is shown in the caution colour.</summary>
    public const double DefaultScaleDb = 12;

    /// <summary>A curve beyond the default scale widens it in these steps to its extreme plus a margin (a +24 dB band: +-27), at most <see cref="MaxScaleDb"/>.</summary>
    private const double ScaleStepDb = 3, ScaleMarginDb = 1.5, MaxScaleDb = 30;

    /// <summary>A peak up to this is no boost (float noise around 0 dB).</summary>
    private const double BoostFromDb = 0.05;

    private const int CurvePoints = 400;

    /// <summary>Where the total response is worked out and drawn: 20 Hz to 20 kHz, evenly on a log scale.</summary>
    public static readonly double[] Freqs = EqMath.LogGrid(CurvePoints, Eq.MinFreq, Eq.MaxFreq);

    public const string NoteText = "Shows EQ and gain only";

    private readonly Func<RowLoad> _readLoad;
    private readonly DispatcherTimer _refresh = new() { Interval = Timing.FxAnalysisRefresh };
    private ChainCurve _curve = new(new double[Freqs.Length], 0, Eq.MinFreq, Array.Empty<string>(), false);
    private FxEngines _engine = FxEngines.Program;
    private RowLoad _load;

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

    /// <summary>The total response at each of <see cref="Freqs"/>, in dB.</summary>
    public IReadOnlyList<double> Curve => _curve.Db;

    /// <summary>The row's effects are switched on (off: the curve is what the chain would do, drawn muted, and nothing is measured).</summary>
    public bool IsOn { get; private set; }

    /// <summary>How far above and below 0 dB the graphs reach: <see cref="DefaultScaleDb"/>, or wider to fit the whole curve.</summary>
    public double ScaleDb { get; private set; } = DefaultScaleDb;

    public string ScaleText => $"±{ScaleDb:0}";

    public double PeakDb => _curve.PeakDb;
    public double PeakHz => _curve.PeakHz;
    public bool HasBoost => IsOn && PeakDb > BoostFromDb;
    public bool PeakIsHigh => IsOn && PeakDb > DefaultScaleDb;

    public string PeakText => IsOn ? DrawKit.ReadoutDb(PeakDb) : "-";
    public string PeakAtText => IsOn ? $"at {DrawKit.ReadoutFreq(PeakHz)}" : string.Empty;

    public string PeakTip =>
        !IsOn ? "Effects are off: the graph shows what the chain would do."
        : HasBoost ? $"Boost of {DrawKit.ReadoutDb(PeakDb)} at about {DrawKit.ReadoutFreq(PeakHz)}. {EngineStatus.HeadroomNote(_engine)}"
        : "No boost: the curve stays at or below 0 dB.";

    public string LatencyText => _load.Place switch
    {
        LoadPlace.Own => $"{_load.DelayMs} ms",
        LoadPlace.Apo => _curve.RunsPlugins ? "not known" : "none added",
        _ => "-",
    };

    public string LatencyTip => _load.Place switch
    {
        LoadPlace.Own => $"About {_load.DelayMs} ms (estimate): recording and playing {_load.DelayMs - _load.PluginDelayMs} ms"
                         + (_load.PluginDelayMs > 0 ? $", plug-ins {_load.PluginDelayMs} ms." : ", no plug-in delay."),
        LoadPlace.Apo => "Equalizer APO adds no delay of its own"
                         + (_curve.RunsPlugins ? "; its plug-ins may add some." : "."),
        _ => NotRunningTip,
    };

    private string NotRunningTip => IsOn ? "Not running now (see the status line)." : "Effects are off.";

    public bool HasNote => _curve.NotDrawn.Count > 0;
    public string NoteTip => "Not in the curve: " + string.Join(", ", _curve.NotDrawn) + ".";

    /// <summary>The chain, the engine that runs the row (<see cref="ModeMethod.Engine"/>) or the row's on/off changed: work the curve out again.</summary>
    /// <param name="apoBlends">Equalizer APO blends a wet mix below 100 % (<see cref="EffectsRules.ApoBlends"/>).</param>
    public void Show(IReadOnlyList<FxSlot> chain, FxEngines engine, bool apoBlends, bool on)
    {
        _engine = engine;
        IsOn = on;
        _curve = ChainResponse.Of(chain, engine, apoBlends, Freqs);
        ScaleDb = ScaleFor(_curve.Db);
        Notify(nameof(IsOn), nameof(ScaleDb), nameof(ScaleText), nameof(PeakDb), nameof(PeakHz), nameof(HasBoost), nameof(PeakIsHigh),
            nameof(PeakText), nameof(PeakAtText), nameof(PeakTip), nameof(HasNote), nameof(NoteTip), nameof(LatencyText), nameof(LatencyTip));
        Redraw?.Invoke();
    }

    /// <summary>The scale that shows the whole curve: the default, or the curve's extreme plus a margin, in whole steps, held at the most.</summary>
    private static double ScaleFor(IReadOnlyList<double> curve)
    {
        var extreme = curve.Max(Math.Abs);
        if (extreme <= DefaultScaleDb) return DefaultScaleDb;
        return Math.Min(MaxScaleDb, Math.Ceiling((extreme + ScaleMarginDb) / ScaleStepDb) * ScaleStepDb);
    }

    /// <summary>The total response at <paramref name="hz"/> (between two of <see cref="Freqs"/>, read straight across on the log scale).</summary>
    public double DbAt(double hz)
    {
        var at = Math.Log(hz / Freqs[0]) / Math.Log(Freqs[^1] / Freqs[0]) * (Freqs.Length - 1);
        var i = Math.Clamp((int)Math.Floor(at), 0, Freqs.Length - 2);
        var share = Math.Clamp(at - i, 0, 1);
        return Curve[i] + (Curve[i + 1] - Curve[i]) * share;
    }

    /// <summary>The engine's status for this row changed, or the reading period ended: read where it runs and its delay.</summary>
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
