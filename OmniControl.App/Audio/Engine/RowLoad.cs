namespace OmniControl.App.Audio.Engine;

/// <summary>Where a row's effects are processed right now.</summary>
public enum LoadPlace
{
    /// <summary>Nowhere: effects off, nothing to run, or the engine is not running (waiting, failed, not installed).</summary>
    None,
    /// <summary>Inside Windows by Equalizer APO (the master): OmniControl adds no delay and does not see the processing.</summary>
    Apo,
    /// <summary>In OmniControl's own engine.</summary>
    Own,
}

/// <summary>
/// Where a row's effects run now and the delay they add, for the FX window's Latency (<see cref="EffectsEngine.LoadOf"/>). For
/// <see cref="LoadPlace.Own"/>: the estimated delay of the processed sound (<paramref name="DelayMs"/>, including the plug-ins'
/// reported latency <paramref name="PluginDelayMs"/>).
/// </summary>
public readonly record struct RowLoad(LoadPlace Place, int DelayMs, int PluginDelayMs)
{
    public static RowLoad None => default;
    public static RowLoad InApo => new(LoadPlace.Apo, 0, 0);
}

/// <summary>
/// What a running compressor or limiter measures now (<see cref="EffectsEngine.ReadingOf"/>): how far it turns the sound down
/// (<paramref name="ReductionDb"/>, 0 or more) and the level its meter shows (<paramref name="LevelDb"/> in dBFS: the compressor's
/// input, the limiter's output). Both fall slowly, so a short peak can still be read.
/// </summary>
public readonly record struct DynamicsReading(double ReductionDb, double LevelDb);
