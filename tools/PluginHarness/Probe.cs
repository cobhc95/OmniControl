using System.Text.RegularExpressions;
using OmniControl.Core.Engine;

namespace PluginHarness;

/// Everything measured for one plug-in file (a child process fills it; the parent turns it into a table row).
internal sealed class Result
{
    public string File { get; set; } = "";
    public string Format { get; set; } = "";
    public bool Loaded { get; set; }
    public string Error { get; set; } = "";
    public string Name { get; set; } = "";
    public int Inputs { get; set; }
    public int Outputs { get; set; }
    public int Params { get; set; }
    public string Chunks { get; set; } = "n/a";
    public int LatencyFrames { get; set; }
    public int StateBytes { get; set; }
    public bool Silent { get; set; }
    public bool IdenticalToInput { get; set; }
    public double OutRmsDb { get; set; }
    public double NullDb { get; set; }
    public double MaxBandDb { get; set; }
    public int NonFinite { get; set; }
    public string ChangeMode { get; set; } = "";
    public bool ChangeAudible { get; set; }
    public double ChangeMaxBandDb { get; set; }
    public string RestoreResult { get; set; } = "";
    public double RestoreMaxBandDb { get; set; }
    public int ParamsNotRestored { get; set; }
    public string Exit { get; set; } = "";
}

/// Runs the plug-in the way the engine does: PluginLoader.Load at 48 kHz with Core's block limit, then Process in 512-frame blocks.
internal static class Probe
{
    /// A band change above this (dB) counts as an audible change.
    private const double Audible = 0.1;

    private static readonly Regex Named = new("gain|drive|mix|level|amount|output|input|wet|dry|threshold", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Result Run(string path)
    {
        var format = path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ? PluginFormat.Vst3 : PluginFormat.Vst2;
        var r = new Result { File = path, Format = format.ToString().ToUpperInvariant() };
        var input = Analysis.TestSignal();
        var inBands = Analysis.Bands(input);

        IPluginEffect? first = null;
        float[] baseline;
        double[] baseBands;
        try
        {
            first = PluginLoader.Load(format, path, Analysis.Rate, OmniControl.Core.CoreTiming.PipeMaxBlockFrames);
            r.Loaded = true;
            r.Name = first.Name;
            r.Inputs = first.InputChannels;
            r.Outputs = first.OutputChannels;
            r.Params = first.Parameters.Count;
            r.LatencyFrames = first.LatencyFrames;
            try
            {
                var state = first.SaveState();
                r.StateBytes = state.Length;
                if (format == PluginFormat.Vst2) r.Chunks = Vst2Settings.Read(state)?.Chunk is not null ? "yes" : "no (values)";
            }
            catch (InvalidOperationException ex) { r.Chunks = "SaveState failed: " + ex.Message; }
            baseline = Process(first, input);
            baseBands = Analysis.Bands(baseline);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            r.Error = ex.Message;
            return r;
        }
        finally
        {
            first?.Dispose();
        }

        r.NonFinite = Analysis.NonFinite(baseline);
        r.OutRmsDb = Analysis.Db(Analysis.Rms(baseline, 0));
        r.Silent = r.OutRmsDb < -100;
        r.NullDb = Analysis.NullDb(input, baseline, r.LatencyFrames);
        r.IdenticalToInput = r.NullDb < -90;
        r.MaxBandDb = Analysis.MaxBandChange(inBands, baseBands);

        try
        {
            Change(path, format, input, baseBands, r);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            r.RestoreResult = "failed: " + ex.Message;
        }
        return r;
    }

    // Changed sound on one instance, then its saved state restored into a fresh instance (what Equalizer APO is handed).
    private static void Change(string path, PluginFormat format, float[] input, double[] baseBands, Result r)
    {
        var tries = new List<(string Mode, Action<IPluginEffect> Set)> { ("all parameters 0.75", p => { foreach (var q in p.Parameters) p.Set(q.Id, 0.75); }) };
        foreach (var named in new[] { 0.0, 1.0 })
            tries.Add(($"first gain-like parameter {named:0}", p =>
            {
                if (p.Parameters.FirstOrDefault(q => Named.IsMatch(q.Title)) is { } q) p.Set(q.Id, named);
            }));

        byte[]? bestState = null;
        float[]? bestOut = null;
        var best = -1.0;
        foreach (var (mode, set) in tries)
        {
            using var effect = PluginLoader.Load(format, path, Analysis.Rate, OmniControl.Core.CoreTiming.PipeMaxBlockFrames);
            set(effect);
            var output = Process(effect, input);
            var change = Analysis.MaxBandChange(baseBands, Analysis.Bands(output));
            if (change > best)
            {
                best = change;
                r.ChangeMode = mode;
                bestOut = output;
                bestState = SafeState(effect);
            }
            if (change > Audible) break;
        }
        r.ChangeMaxBandDb = Math.Max(0, best);
        r.ChangeAudible = best > Audible;
        if (bestState is null || bestOut is null) { r.RestoreResult = "no state to restore"; return; }

        using var fresh = PluginLoader.Load(format, path, Analysis.Rate, OmniControl.Core.CoreTiming.PipeMaxBlockFrames);
        fresh.LoadState(bestState);
        var restored = Process(fresh, input);
        r.RestoreMaxBandDb = Analysis.MaxBandChange(Analysis.Bands(bestOut), Analysis.Bands(restored));
        r.RestoreResult = r.RestoreMaxBandDb <= Audible ? "same" : r.ChangeAudible ? "DIFFERENT" : "n/a (no audible change to restore)";
    }

    private static byte[]? SafeState(IPluginEffect effect)
    {
        try { return effect.SaveState(); }
        catch (InvalidOperationException) { return null; }
    }

    /// The first output channel of the whole test signal, processed in blocks of <see cref="Analysis.Block"/> frames.
    private static float[] Process(IPluginEffect effect, float[] mono)
    {
        int ins = effect.InputChannels, outs = effect.OutputChannels;
        var inBuffer = new float[Analysis.Block * ins];
        var outBuffer = new float[Analysis.Block * outs];
        var result = new float[mono.Length];
        for (var start = 0; start < mono.Length; start += Analysis.Block)
        {
            var frames = Math.Min(Analysis.Block, mono.Length - start);
            for (var f = 0; f < frames; f++)
                for (var c = 0; c < ins; c++) inBuffer[f * ins + c] = mono[start + f];
            effect.Process(inBuffer, outBuffer, frames);
            for (var f = 0; f < frames; f++) result[start + f] = outBuffer[f * outs];
        }
        return result;
    }
}
