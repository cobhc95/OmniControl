namespace OmniControl.App.Audio.Engine;

/// <summary>Running totals of a row's processing: time spent and real time of the sound processed, both in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
public readonly record struct CpuSample(long BusyTicks, long AudioTicks)
{
    /// <summary>
    /// The share of one CPU core the processing took between <paramref name="earlier"/> and this reading (0.01 = 1 %): time spent
    /// divided by the real time of the sound processed; null when no sound was processed in between.
    /// </summary>
    public double? ShareSince(CpuSample earlier)
    {
        var audio = AudioTicks - earlier.AudioTicks;
        return audio <= 0 ? null : (double)(BusyTicks - earlier.BusyTicks) / audio;
    }
}

/// <summary>Where a row's effects are processed right now.</summary>
public enum LoadPlace
{
    /// <summary>Nowhere: effects off, nothing to run, or the engine is not running (waiting, failed, not installed).</summary>
    None,
    /// <summary>Inside Windows by Equalizer APO (Fast mode's master and microphones): OmniControl adds no delay and does not see the processing.</summary>
    Apo,
    /// <summary>In OmniControl's own engine.</summary>
    Own,
}

/// <summary>
/// What a row's effects cost now, for the FX window's numbers (<see cref="EffectsEngine.LoadOf"/>). For <see cref="LoadPlace.Own"/>:
/// the estimated delay of the processed sound (<paramref name="DelayMs"/>, including the plug-ins' reported latency
/// <paramref name="PluginDelayMs"/>) and the processing totals.
/// </summary>
public readonly record struct RowLoad(LoadPlace Place, int DelayMs, int PluginDelayMs, CpuSample Cpu)
{
    public static RowLoad None => default;
    public static RowLoad InApo => new(LoadPlace.Apo, 0, 0, default);
}
