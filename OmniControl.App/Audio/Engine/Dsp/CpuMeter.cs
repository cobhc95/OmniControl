using System.Diagnostics;

namespace OmniControl.App.Audio.Engine.Dsp;

/// <summary>
/// Counts how long a row's processing takes against how much sound it processed: the audio thread adds each block (<see cref="Add"/>:
/// two <see cref="Interlocked.Add(ref long, long)"/>, no lock, no allocation), the UI reads the running totals (<see cref="Read"/>) and
/// compares two readings (<see cref="CpuSample.ShareSince"/>). The two totals are not read as one, so a reading can be one block
/// apart; over the FX window's reading period that is a small fraction.
/// </summary>
internal sealed class CpuMeter
{
    private long _busy, _audio;

    /// <summary>Audio thread: one block took <paramref name="busyTicks"/> (<see cref="Stopwatch"/> ticks) for <paramref name="frames"/> frames at <paramref name="sampleRate"/>.</summary>
    public void Add(long busyTicks, int frames, int sampleRate)
    {
        Interlocked.Add(ref _busy, busyTicks);
        Interlocked.Add(ref _audio, frames * Stopwatch.Frequency / sampleRate);
    }

    public CpuSample Read() => new(Interlocked.Read(ref _busy), Interlocked.Read(ref _audio));
}
