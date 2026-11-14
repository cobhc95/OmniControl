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

/// <summary>
/// A row's effects status: the state and the sentence the FX window shows; <paramref name="Detail"/> = what Windows answered, kept out
/// of the sentence and shown only in the full line (<see cref="LineFor"/>, the tooltips). The Mixer row and the Wiring tab show
/// <see cref="Brief"/> instead, built from <paramref name="Kind"/>, <paramref name="Method"/> and the (steady) <paramref name="DelayMs"/>.
/// </summary>
public sealed record EngineStatus(EngineState State, string Text, string? Detail = null, StatusKind Kind = StatusKind.Other,
    RunMethod Method = RunMethod.None, int? DelayMs = null)
{
    public static readonly EngineStatus Off = new(EngineState.Off, string.Empty);

    /// <summary>The one short line of a Mixer row ("Recording · 70 ms"); empty when the effects are off.</summary>
    public string Brief => Kind != StatusKind.Other ? SoundModes.Brief(Kind, Method, DelayMs)
        : State switch
        {
            EngineState.Waiting => SoundModes.Brief(StatusKind.Starting),
            EngineState.Running => SoundModes.Brief(StatusKind.Running, Method, DelayMs),
            EngineState.Unavailable => SoundModes.NotAvailable,
            EngineState.Failed => SoundModes.Brief(StatusKind.Stopped),
            _ => string.Empty,
        };

    /// <summary>The tooltip of <see cref="Brief"/>: the full line (<see cref="LineFor"/>) and, where there is one, what the method means or what to do next.</summary>
    public string TipFor(SoundMode mode, ModeMethod method) =>
        SoundModes.Advice(Kind, Method) is { } advice ? $"{LineFor(mode, method)}{Environment.NewLine}{advice}" : LineFor(mode, method);

    public bool IsRunning => State == EngineState.Running;
    public bool IsProblem => State is EngineState.Failed or EngineState.Unavailable;

    /// <summary>
    /// The one line a row and the FX window show: the mode, how the mode runs this row's effects, and what is really happening. A
    /// problem is said first and alone (nothing is implied to run); with effects off the method is only what would happen.
    /// </summary>
    /// <param name="method">How the row's effects run in this mode (<see cref="EffectsEngine.MethodFor"/>).</param>
    public string LineFor(SoundMode mode, ModeMethod method)
    {
        return State switch
        {
            EngineState.Off when Text.Length > 0 => $"{mode.Name} mode · {Text}",
            EngineState.Off => $"{mode.Name} mode · effects off · when on: {method.How}, {method.Delay}",
            EngineState.Failed or EngineState.Unavailable => Detail is null ? $"{mode.Name} mode · {Text}" : $"{mode.Name} mode · {Text} · {Detail}",
            _ => $"{mode.Name} mode · effects {method.How} · {Text}",
        };
    }

    /// <summary>
    /// What each engine does so that a boost cannot clip (the FX window's Peak): OmniControl's engine turns loud peaks down at the end
    /// (<see cref="Dsp.PeakLimiter"/>) and lowers nothing in advance; in Equalizer APO each equaliser's bands follow a Preamp of minus
    /// their highest boost (<see cref="ApoChainWriter"/>), and there is no limiter.
    /// </summary>
    public static string HeadroomNote(FxEngines engine) => engine == FxEngines.Apo
        ? "Equalizer APO lowers each equaliser by its highest boost. Overall gain above 0 dB and Gain effects are not lowered, and there is no limiter."
        : $"OmniControl lowers nothing in advance; its limiter keeps the output below {Dsp.PeakLimiter.CeilingDb:0} dB.";

    /// <summary>The note about effects in the chain that this engine does not run (kept in the chain, saved only), added to a running status.</summary>
    public static string SkippedNote(IReadOnlyList<string> skipped) =>
        skipped.Count == 0 ? string.Empty : $" · saved only, not run here: {string.Join(", ", skipped)}";
}
