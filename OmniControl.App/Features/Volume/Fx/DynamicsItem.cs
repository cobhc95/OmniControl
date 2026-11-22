using System.Globalization;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Fx;

/// The Compressor's and the Limiter's graph card (design and wording from TabForge's compressor and limiter windows, MIT): for the
/// compressor the transfer curve (output level against input level, with a dot at the input level now) beside two meters, gain reduction
/// and input level; for the limiter the same two meters (gain reduction, output level) and its look-ahead line. The curve is drawn from
/// the slot's own settings with the stage's own gain computer; the meters and the dot read the stage that processes the sound
/// (<see cref="EffectsEngine.ReadingOf"/>) every <see cref="Timing.MeterFrame"/> while the card is on screen, and when nothing runs
/// here they show nothing and say so. Numbers only; the drawing is <see cref="TransferGraph"/> and <see cref="DynamicsMeter"/>.
public sealed partial class DynamicsItem : ObservableObject, IEditorItem, IRedrawSource
{
    /// The meters' scales (TabForge's): the bar for gain reduction is full at this many dB, the bar for a level is empty at its floor.
    private const double CompressorReductionRangeDb = 24, CompressorLevelFloorDb = -60, LimiterReductionRangeDb = 12, LimiterLevelFloorDb = -36;

    /// A level from here up is drawn in the caution colour, and (compressor) in the danger colour from <see cref="CompressorClipDb"/>.
    private const double CompressorWarmDb = -6, CompressorClipDb = -0.5, LimiterWarmDb = -3, LimiterOverCeilingDb = 0.2;

    /// The transfer curve runs from this input level to 0 dB; below it the dot is left out.
    public const double CurveFloorDb = -60;

    private readonly FxSlot _slot;
    private readonly Func<DynamicsReading?> _read;
    private readonly DispatcherTimer _poll = new() { Interval = Timing.MeterFrame };
    private DynamicsReading? _reading;
    private string _reductionText = "-", _levelText = "-";

    public DynamicsItem(FxSlot slot, Func<DynamicsReading?> read)
    {
        _slot = slot;
        _read = read;
        _poll.Tick += (_, _) => Poll();
    }

    public string Label => HasCurve ? "Compressor graph and meters" : "Limiter meters";

    /// The compressor draws its transfer curve; the limiter has only meters.
    public bool HasCurve => _slot.Kind == FxCatalog.CompKind;

    public string LevelLabel => HasCurve ? "Input level" : "Output level";

    public event Action? Redraw;

    // ---- what the curve draws (the slot's settings) ----

    public double Threshold => Value(CompressorParams.Threshold);
    public double Ratio => Value(CompressorParams.Ratio);
    public double Knee => Value(CompressorParams.Knee);

    /// The input level the compressor hears now, for the dot; null while nothing is measured or the sound is below the curve's floor.
    public double? InputDb => HasCurve && _reading is { } r && r.LevelDb > CurveFloorDb ? r.LevelDb : null;

    // ---- what the meters show ----

    /// Gain reduction as a share of its bar, 0 to 1 (0 while nothing is measured).
    public double ReductionFraction => _reading is { } r ? Math.Clamp(r.ReductionDb / (HasCurve ? CompressorReductionRangeDb : LimiterReductionRangeDb), 0, 1) : 0;

    /// The level as a share of its bar, 0 to 1 (0 while nothing is measured).
    public double LevelFraction => _reading is { } r ? Math.Clamp((r.LevelDb - LevelFloorDb) / -LevelFloorDb, 0, 1) : 0;

    public DynamicsLevelTone LevelTone => _reading is not { } r ? DynamicsLevelTone.Normal
        : HasCurve ? (r.LevelDb >= CompressorClipDb ? DynamicsLevelTone.Hot : r.LevelDb >= CompressorWarmDb ? DynamicsLevelTone.Warm : DynamicsLevelTone.Normal)
        : (r.LevelDb >= Value(LimiterParams.Ceiling) + LimiterOverCeilingDb ? DynamicsLevelTone.Hot : r.LevelDb >= LimiterWarmDb ? DynamicsLevelTone.Warm : DynamicsLevelTone.Normal);

    public string ReductionText => _reductionText;
    public string LevelText => _levelText;

    /// Why the meters are empty (one plain sentence), or null while they are live.
    public string? Note => _reading is null ? "No live reading: this effect isn't running in OmniControl's engine." : null;

    public bool HasNote => _reading is null;

    /// The limiter's look-ahead line (empty for the compressor and while the limiter does not run).
    public string LatencyText => HasCurve || _reading is null ? string.Empty
        : $"Look-ahead {LimiterParams.LookAheadMs:0.0} ms ({LimiterParams.LookAheadFrames(Core.CoreTiming.PipeSampleRate)} samples): the sound is delayed by this much, counted in the row's Latency.";

    public bool HasLatency => LatencyText.Length > 0;

    private double LevelFloorDb => HasCurve ? CompressorLevelFloorDb : LimiterLevelFloorDb;

    private double Value(int index) => index < _slot.Values.Count ? _slot.Values[index] : FxCatalog.KindOf(_slot)?.Params[index].Default ?? 0;

    // ---- changes ----

    /// A setting changed (a slider, a switch or a preset): the curve draws again, and the limiter's level colour may change.
    public void ValuesChanged()
    {
        OnPropertyChanged(nameof(Threshold));
        OnPropertyChanged(nameof(Ratio));
        OnPropertyChanged(nameof(Knee));
        Redraw?.Invoke();
    }

    /// The card is on screen: read the meters now and every <see cref="Timing.MeterFrame"/>.
    public void StartReading()
    {
        Poll();
        _poll.Start();
    }

    /// The card left the screen.
    public void StopReading() => _poll.Stop();

    private void Poll()
    {
        var reading = _slot.Enabled ? _read() : null;
        // Nothing pushes a new reading, so while there is none or the sound is silent the reads slow down.
        var frame = reading is not { } heard || (heard.LevelDb <= LevelFloorDbShown && heard.ReductionDb <= 0) ? Timing.MeterQuietFrame : Timing.MeterFrame;
        if (_poll.Interval != frame) _poll.Interval = frame;
        var reductionText = reading is { } r ? Db(r.ReductionDb) : "-";
        var levelText = reading is { } l ? (l.LevelDb <= LevelFloorDbShown ? "-inf dB" : Db(l.LevelDb)) : "-";
        if (Same(reading, _reading) && reductionText == _reductionText && levelText == _levelText) return;
        var hadNote = HasNote;
        _reading = reading;
        (_reductionText, _levelText) = (reductionText, levelText);
        OnPropertyChanged(nameof(ReductionText));
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(LatencyText));
        OnPropertyChanged(nameof(HasLatency));
        if (hadNote != HasNote)
        {
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(HasNote));
        }
        Redraw?.Invoke();
    }

    /// The engine's meters bottom out at this level: it reads "-inf dB".
    private const double LevelFloorDbShown = -100;

    private static string Db(double db) => db.ToString("0.0", CultureInfo.InvariantCulture) + " dB";

    /// The same reading to the precision the card draws (half a dB for the dot, a fraction of a pixel for the bars).
    private static bool Same(DynamicsReading? a, DynamicsReading? b) =>
        a is null || b is null ? a is null && b is null
        : Math.Abs(a.Value.ReductionDb - b.Value.ReductionDb) < 0.05 && Math.Abs(a.Value.LevelDb - b.Value.LevelDb) < 0.05;
}

/// How hot a level meter's level is: drawn in the normal, caution or danger colour.
public enum DynamicsLevelTone { Normal, Warm, Hot }
