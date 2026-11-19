using System.Collections.Concurrent;
using OmniControl.App.Features.Volume.Audio.Ir;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// The gain Auto level adds to an IR Loader written for Equalizer APO (the loudest frequency of the file at 0 dB, as in OmniControl's
/// engine), measured on a worker once per file version (<see cref="IrFileStamp"/>) and kept, so reading and analysing a file never runs on
/// the UI thread. Until a version is measured <see cref="Of"/> gives null and the caller waits; <c>measured</c> is raised (on a worker)
/// when it is known. One instance per owner, no shared state.
internal sealed class IrLevels
{
    private readonly ConcurrentDictionary<string, (long Stamp, double Db)> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _measuring = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action _measured;

    /// <param name="measured">A level became known (worker thread): the chain is written again.</param>
    public IrLevels(Action measured) => _measured = measured;

    /// The Auto level gain of this version of the file, or null while it is being measured.
    public double? Of(string path, long stamp)
    {
        if (_known.TryGetValue(path, out var known) && known.Stamp == stamp) return known.Db;
        if (_measuring.TryGetValue(path, out var busy) && busy == stamp) return null;
        _measuring[path] = stamp;
        _ = Guard.RunAsync("Effects: IR file level", () => Task.Run(() =>
        {
            var db = 0.0;
            try
            {
                db = Measure(path);
            }
            finally // an unexpected failure is logged by the guard; the file then plays as it is instead of waiting for ever
            {
                _known[path] = (stamp, db);
                _measuring.TryRemove(new KeyValuePair<string, long>(path, stamp));
                _measured();
            }
        }));
        return null;
    }

    /// 0 when the file cannot be read (Equalizer APO then plays the file as it is).
    private static double Measure(string path)
    {
        try
        {
            var audio = IrWaveFile.Read(path);
            var peak = Math.Max(IrPictures.PeakResponse(audio.Channels[0]), audio.Channels.Length > 1 ? IrPictures.PeakResponse(audio.Channels[1]) : 0);
            return peak > 1e-9 ? Math.Round(-20 * Math.Log10(peak), 2) : 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return 0; // not a WAV this reader takes (a .flac, say): Equalizer APO reads it as it is
        }
    }
}
