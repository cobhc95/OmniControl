namespace OmniControl.App.Audio.Engine;

/// <summary>Where a row's effects stand.</summary>
public enum EngineState
{
    /// <summary>No effects for this row (FX switched off, or nothing in the chain).</summary>
    Off,
    /// <summary>The engine is starting or waiting for the program to play.</summary>
    Waiting,
    /// <summary>The effects are processing the sound now.</summary>
    Running,
    /// <summary>What the row needs is not installed or not possible here: the sound plays without effects.</summary>
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
    public string Brief => Kind != StatusKind.Other ? EffectsRules.Brief(Kind, Method, DelayMs)
        : State switch
        {
            EngineState.Waiting => EffectsRules.Brief(StatusKind.Starting),
            EngineState.Running => EffectsRules.Brief(StatusKind.Running, Method, DelayMs),
            EngineState.Unavailable => EffectsRules.NotAvailable,
            EngineState.Failed => EffectsRules.Brief(StatusKind.Stopped),
            _ => string.Empty,
        };

    /// <summary>The short fact under a Mixer row's Method chip ("70 ms", "no delay"; <see cref="EffectsRules.FactOf"/>); <paramref name="chip"/> = the chip's method.</summary>
    public string Fact(RunMethod chip) =>
        EffectsRules.FactOf(Kind == StatusKind.Other && State == EngineState.Running ? StatusKind.Running : Kind, chip, DelayMs, Brief);

    /// <summary>The tooltip of <see cref="Brief"/>: the full line (<see cref="LineFor"/>) and, where there is one, what the method means or what to do next.</summary>
    public string TipFor(ModeMethod method) =>
        EffectsRules.Advice(Kind, Method) is { } advice ? $"{LineFor(method)}{Environment.NewLine}{advice}" : LineFor(method);

    public bool IsRunning => State == EngineState.Running;
    public bool IsProblem => State is EngineState.Failed or EngineState.Unavailable;

    /// <summary>
    /// The one line a row and the FX window show: how it runs this row's effects, and what is really happening. A problem is said
    /// first and alone (nothing is implied to run); with effects off the method is only what would happen.
    /// </summary>
    /// <param name="method">How the row's effects run (<see cref="EffectsEngine.MethodFor"/>).</param>
    /// <param name="fxOn">The row's effects switch is on: an idle engine then says nothing is running yet, never "off".</param>
    public string LineFor(ModeMethod method, bool fxOn = false) => State switch
    {
        EngineState.Off when Text.Length > 0 => Text,
        EngineState.Off when fxOn => $"Effects on · nothing running yet: {method.How}, {method.Delay}",
        EngineState.Off => $"Effects off · when on: {method.How}, {method.Delay}",
        EngineState.Failed or EngineState.Unavailable => Detail is null ? Text : $"{Text} · {Detail}",
        _ => $"Effects {method.How} · {Text}",
    };

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
