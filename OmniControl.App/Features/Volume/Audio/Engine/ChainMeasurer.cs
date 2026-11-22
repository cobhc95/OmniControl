using OmniControl.App.Features.Volume.Audio.Engine.Dsp;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// What a measurement of a whole chain found (the FX window's charts): <paramref name="Db"/> at each frequency asked for, its highest
/// point, the delay when the test signal showed it clearly (null = not known), how long the sound rings on after the signal stops,
/// and the facts the curve does not show: a level-dependent effect (measured at <see cref="ChainMeasurer.TestLevelDb"/>), Equalizer
/// APO effects of its own that are not in it, and the switched-on effects that could not be measured, each with its reason.
public sealed record ChainMeasurement(double[] Db, double PeakDb, double PeakHz, int? LatencyMs, double TailSeconds,
    bool LevelDependent, bool ApoOwnLeftOut, IReadOnlyList<string> NotMeasured);

/// One measurement's input, taken on the UI thread (<see cref="EffectsEngine.MeasureJobOf"/>): copies of the slots that really run, in
/// order (the live chain and its plug-ins are never touched), with each plug-in's current settings in its copy.
internal sealed record ChainMeasureJob(IReadOnlyList<FxPart> Parts, IReadOnlyList<string> NotRun, bool LevelDependent, bool ApoOwnLeftOut)
{
    /// Two jobs with the same text measure the same: an unchanged chain is not measured again.
    public string Signature { get; } = string.Join('\n', Parts.SelectMany(p => (p.Folder is { } f ? new[] { f } : Array.Empty<FxSlot>()).Concat(p.Effects))
        .Select(s => $"{s.Kind}|{s.Enabled}|{s.Wet}|{string.Join(',', s.Values)}|{s.Path}|{s.State}|"
                     + string.Join(';', (s.Bands ?? new List<EqBand>()).Select(b => $"{b.Enabled},{b.Type},{b.Freq},{b.Gain},{b.Q}"))))
        + $"\n{LevelDependent}{ApoOwnLeftOut}{string.Join(',', NotRun)}";
}

/// Measures a chain by playing a short log sine sweep (<see cref="SweepSeconds"/> at <see cref="TestLevelDb"/>) through it once, in
/// order, then silence until it rings out: the response is the output's spectrum over the sweep's, smoothed to a twelfth of an octave;
/// the delay is where the output lines up best with the sweep; the tail is how long the output stays above <see cref="TailFloorDb"/>
/// below its peak after the sweep. Runs on its own below-normal, STA thread (plug-ins are made there and released after), never on
/// the audio path. One instance per owner (an FX window) runs at most one measurement at a time and keeps only the latest request
/// waiting: a plug-in call that hangs can't be stopped, so a second set of plug-ins is never loaded beside it.
internal sealed class ChainMeasurer
{
    private sealed record Request(ChainMeasureJob Job, IReadOnlyList<double> Freqs, CancellationToken Cancel, TaskCompletionSource<ChainMeasurement?> Done);

    private readonly object _gate = new();
    private Request? _waiting;
    private bool _running;

    public const double TestLevelDb = -18;
    private const double SweepSeconds = 0.5, SweepFromHz = 10, SweepToHz = 22000, FadeSeconds = 0.005;
    private const int FftSize = 1 << 16;
    private const double MaxTailSeconds = 4, TailQuietSeconds = 0.25, TailFloorDb = -60;
    private const double SmoothOctaves = 1.0 / 24, FloorDb = -120;

    /// A delay is reported only when the line-up peak stands this far above the average (else "not known").
    private const double LatencyClarity = 8;

    private const int Rate = PipeFormat.SampleRate, Block = Core.CoreTiming.PipeMaxBlockFrames;

    /// Measures <paramref name="job"/> once the measurement before it has released its plug-ins; null when cancelled, replaced by a
    /// newer request before it started, or failed (logged).
    public Task<ChainMeasurement?> RunAsync(ChainMeasureJob job, IReadOnlyList<double> freqs, CancellationToken cancel)
    {
        var request = new Request(job, freqs, cancel, new TaskCompletionSource<ChainMeasurement?>(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (_gate)
        {
            _waiting?.Done.TrySetResult(null);
            _waiting = request;
            if (_running) return request.Done.Task;
            _running = true;
        }
        var thread = new Thread(Work) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "FX chain measurement" };
        thread.SetApartmentState(ApartmentState.STA); // VST3 plug-ins are COM objects
        thread.Start();
        return request.Done.Task;
    }

    /// Takes the waiting request until none is left; each one's plug-ins are released before the next is taken.
    private void Work()
    {
        while (true)
        {
            Request request;
            lock (_gate)
            {
                if (_waiting is null) { _running = false; return; }
                request = _waiting;
                _waiting = null;
            }
            ChainMeasurement? result = null;
            Guard.Run("Effects: measuring a chain", () =>
            {
                try { result = Run(request.Job, request.Freqs, request.Cancel); }
                catch (OperationCanceledException) { result = null; }
            });
            request.Done.TrySetResult(result);
        }
    }

    private static ChainMeasurement Run(ChainMeasureJob job, IReadOnlyList<double> freqs, CancellationToken cancel)
    {
        var plugins = new Dictionary<FxSlot, (IPluginEffect? Effect, string? Error)>(ReferenceEqualityComparer.Instance);
        try
        {
            var chain = ChainBuilder.BuildParts(job.Parts, slot => plugins.TryGetValue(slot, out var p) ? p : plugins[slot] = PluginChainHost.Load(slot), loadHere: true, cancel: cancel);
            var (db, latency, tail) = Measure(chain, freqs, cancel);
            var peak = 0;
            for (var i = 1; i < db.Length; i++) if (db[i] > db[peak]) peak = i;
            return new ChainMeasurement(db, db[peak], freqs[peak], latency is { } frames ? PipeFormat.ToMs(frames) : null, tail,
                job.LevelDependent, job.ApoOwnLeftOut, job.NotRun.Concat(chain.Skipped).ToList());
        }
        finally
        {
            foreach (var (effect, _) in plugins.Values) effect?.Dispose();
        }
    }

    /// The response of <paramref name="chain"/> at <paramref name="freqs"/> (dB), its delay in frames (null when unclear) and its tail in seconds.
    public static (double[] Db, int? LatencyFrames, double TailSeconds) Measure(DspChain chain, IReadOnlyList<double> freqs, CancellationToken cancel)
    {
        var sweepFrames = (int)(SweepSeconds * Rate);
        var input = Sweep(sweepFrames);
        var outL = new float[FftSize];
        var outR = new float[FftSize];
        var buffer = new float[Block * PipeFormat.Channels];
        var maxFrames = sweepFrames + (int)(MaxTailSeconds * Rate);
        var quietFrames = (int)(TailQuietSeconds * Rate);
        float peak = 0;
        var lastLoud = 0;
        for (var at = 0; at < maxFrames; at += Block)
        {
            cancel.ThrowIfCancellationRequested();
            for (var f = 0; f < Block; f++) buffer[2 * f] = buffer[2 * f + 1] = at + f < sweepFrames ? input[at + f] : 0;
            chain.Process(buffer, PipeFormat.Channels);
            var floor = peak * (float)DspMath.DbToLin(TailFloorDb);
            for (var f = 0; f < Block; f++)
            {
                float l = buffer[2 * f], r = buffer[2 * f + 1];
                if (at + f < FftSize) { outL[at + f] = l; outR[at + f] = r; }
                var level = Math.Max(Math.Abs(l), Math.Abs(r));
                if (!float.IsFinite(level)) continue;
                if (level > peak) peak = level;
                if (level > floor) lastLoud = at + f;
            }
            if (at >= FftSize && at - lastLoud > quietFrames) break;
        }
        var db = Response(input, outL, outR, freqs, out var latency);
        return (db, latency, Math.Max(0, lastLoud - sweepFrames) / (double)Rate);
    }

    /// The exponential sweep at the test level, faded in and out so its ends do not click.
    private static float[] Sweep(int frames)
    {
        var sweep = new float[FftSize];
        var amplitude = DspMath.DbToLin(TestLevelDb);
        var spread = Math.Log(SweepToHz / SweepFromHz);
        var fade = (int)(FadeSeconds * Rate);
        for (var n = 0; n < frames; n++)
        {
            var t = n / (double)Rate;
            var phase = 2 * Math.PI * SweepFromHz * SweepSeconds / spread * (Math.Exp(t * spread / SweepSeconds) - 1);
            var edge = Math.Min(1, Math.Min(n, frames - 1 - n) / (double)fade);
            sweep[n] = (float)(amplitude * edge * Math.Sin(phase));
        }
        return sweep;
    }

    /// Output power over input power around each frequency (both channels), and the lag where output and input line up best.
    private static double[] Response(float[] input, float[] outL, float[] outR, IReadOnlyList<double> freqs, out int? latency)
    {
        const int bins = FftSize / 2 + 1, half = FftSize / 2;
        var fft = new RealFft(FftSize);
        float[] xRe = new float[bins], xIm = new float[bins], lRe = new float[bins], lIm = new float[bins], rRe = new float[bins], rIm = new float[bins];
        fft.Forward(input.AsSpan(0, half), input.AsSpan(half), xRe, xIm);
        fft.Forward(outL.AsSpan(0, half), outL.AsSpan(half), lRe, lIm);
        fft.Forward(outR.AsSpan(0, half), outR.AsSpan(half), rRe, rIm);

        var db = new double[freqs.Count];
        var hzPerBin = (double)Rate / FftSize;
        for (var i = 0; i < db.Length; i++)
        {
            var from = Math.Max(1, (int)Math.Floor(freqs[i] * Math.Pow(2, -SmoothOctaves) / hzPerBin));
            var to = Math.Min(bins - 1, Math.Max(from, (int)Math.Ceiling(freqs[i] * Math.Pow(2, SmoothOctaves) / hzPerBin)));
            double inPower = 0, outPower = 0;
            for (var k = from; k <= to; k++)
            {
                inPower += 2 * ((double)xRe[k] * xRe[k] + (double)xIm[k] * xIm[k]);
                outPower += (double)lRe[k] * lRe[k] + (double)lIm[k] * lIm[k] + (double)rRe[k] * rRe[k] + (double)rIm[k] * rIm[k];
            }
            db[i] = inPower > 0 && outPower > 0 ? Math.Max(FloorDb, 10 * Math.Log10(outPower / inPower)) : FloorDb;
        }

        // Cross-correlation of (left + right) with the sweep, reusing the left arrays: Y X*.
        for (var k = 0; k < bins; k++)
        {
            float yr = lRe[k] + rRe[k], yi = lIm[k] + rIm[k];
            lRe[k] = yr * xRe[k] + yi * xIm[k];
            lIm[k] = yi * xRe[k] - yr * xIm[k];
        }
        fft.Inverse(lRe, lIm, outL);
        var best = 0;
        double sum = 0;
        for (var n = 0; n < half; n++)
        {
            sum += Math.Abs(outL[n]);
            if (Math.Abs(outL[n]) > Math.Abs(outL[best])) best = n;
        }
        latency = sum > 0 && Math.Abs(outL[best]) > LatencyClarity * sum / half ? best : null;
        return db;
    }
}
