namespace OmniControl.App.Audio.Engine;

/// <summary>Where a row's effects stand.</summary>
public enum EngineState
{
    /// <summary>No effects for this row (mode Off, FX switched off, or nothing in the chain).</summary>
    Off,
    /// <summary>The engine is starting or waiting for the program to play.</summary>
    Waiting,
    /// <summary>The effects are processing the sound now.</summary>
    Running,
    /// <summary>The mode is not installed or not possible here: the sound plays without effects.</summary>
    Unavailable,
    /// <summary>The engine stopped with an error and put the original sound back.</summary>
    Failed,
}

/// <summary>A row's effects status: the state and the short sentence the row and the FX window show.</summary>
public sealed record EngineStatus(EngineState State, string Text)
{
    public static readonly EngineStatus Off = new(EngineState.Off, string.Empty);

    public bool IsRunning => State == EngineState.Running;
    public bool IsProblem => State is EngineState.Failed or EngineState.Unavailable;

    /// <summary>
    /// The one line a row and the FX window show: the mode, how the mode runs this row's effects, and what is really happening. A
    /// problem is said first and alone (nothing is implied to run); with effects off the method is only what would happen.
    /// </summary>
    /// <param name="key">The row: null = the master, an input key = a microphone, else a program or combined device.</param>
    public string LineFor(SoundMode mode, string? key)
    {
        var method = mode.MethodFor(key);
        return State switch
        {
            EngineState.Off => $"{mode.Name} mode · effects off · when on: {method.How}, {method.Delay}",
            EngineState.Failed or EngineState.Unavailable => $"{mode.Name} mode · {Text}",
            _ => $"{mode.Name} mode · effects {method.How} · {Text}",
        };
    }

    /// <summary>
    /// What each engine does so that a boost cannot clip (the FX window's Peak): OmniControl's engine turns loud peaks down at the end
    /// (<see cref="Dsp.PeakLimiter"/>) and lowers nothing in advance; in Equalizer APO each equaliser's bands follow a Preamp of minus
    /// their highest boost (<see cref="ApoChainWriter"/>), and there is no limiter.
    /// </summary>
    public static string HeadroomNote(FxEngines engine) => engine == FxEngines.Apo
        ? "In Equalizer APO each equaliser's bands get a level cut as large as their highest boost, so the bands cannot clip; an overall gain above 0 dB and Gain effects are not cut, and Equalizer APO has no limiter."
        : $"OmniControl does not lower the level for it: its safety limiter turns loud peaks down so the output stays below {Dsp.PeakLimiter.CeilingDb:0} dB and cannot clip.";

    /// <summary>The note about effects in the chain that this engine does not run (kept in the chain, saved only), added to a running status.</summary>
    public static string SkippedNote(IReadOnlyList<string> skipped) =>
        skipped.Count == 0 ? string.Empty : $" · saved only, not run here: {string.Join(", ", skipped)}";
}
