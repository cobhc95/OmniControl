namespace OmniControl.App.Features.Volume.Audio;

/// What a Mixer row stands for, as far as its Method chip is concerned.
public enum RowKind { Master, Program, Microphone, Combined }

/// The live facts a Mixer row's Method chip is worked out from (<see cref="EffectsRules.ChipMethodOf"/>).
/// <param name="Runs">The row's effects are on and at least one of them is switched on.</param>
/// <param name="Running">What the engine runs for this row now (<see cref="Engine.EffectsEngine.MethodOf"/>; none while nothing runs).</param>
/// <param name="Master">How the master runs now (<see cref="Engine.EffectsEngine.MasterMethod"/>).</param>
/// <param name="FollowsMaster">A program that plays where the master plays (its own "plays on" list is empty).</param>
/// <param name="Route">A program's chosen method.</param>
/// <param name="CableFree">A cable pair is free for this program now (Automatic then takes it).</param>
/// <param name="Own">How a microphone runs (<see cref="Engine.EffectsEngine.MicMethodOf"/>).</param>
public sealed record RowMethodFacts(RowKind Kind, bool Runs, RunMethod Running, RunMethod Master, bool FollowsMaster = false,
    EffectsRoute? Route = null, bool CableFree = false, RunMethod Own = RunMethod.Cable);

/// The one home of a Mixer row's Method chip and the short fact under it: which method the row's
/// sound really goes through (what runs now, else what will run when it plays), its words, and the fact ("70 ms", "no delay", "on the
/// speakers"); the method's word is on the chip, so the fact never repeats it. Pure: the page passes the live facts in.
public static partial class EffectsRules
{
    /// The chip's method: what the engine runs now; otherwise the master on the cable bus or in Equalizer APO; a program without effects
    /// passes through the cable bus when it follows a master on it, else plays direct; a program with effects takes its chosen method
    /// (Automatic: a free cable, else recording); a microphone's chip is its method, as the master's; a combined device always uses its cable.
    public static RunMethod ChipMethodOf(RowMethodFacts f)
    {
        if (f.Running != RunMethod.None) return f.Running;
        return f.Kind switch
        {
            // The master's chip is the method it is set to (never "Direct": the menu has no such choice), also with no effects.
            RowKind.Master => f.Master == RunMethod.Bus ? RunMethod.Bus : RunMethod.Apo,
            RowKind.Program when !f.Runs => f.Master == RunMethod.Bus && f.FollowsMaster ? RunMethod.Bus : RunMethod.None,
            RowKind.Program => ProgramRouteMethod(f.Route ?? RouteAutomatic, f.CableFree),
            RowKind.Microphone => f.Own,
            _ => RunMethod.Cable,
        };
    }

    private static RunMethod ProgramRouteMethod(EffectsRoute route, bool cableFree) =>
        route == RouteCable ? RunMethod.Cable
        : route == RouteCapture ? RunMethod.Recording
        : route == RouteApoCable ? RunMethod.ApoCable
        : cableFree ? RunMethod.Cable : RunMethod.Recording;

    /// The chip's words for a method ("Direct" when nothing processes the sound).
    public static string ChipWord(RunMethod method) => method switch
    {
        RunMethod.Recording => "Recording",
        RunMethod.Cable => "Cable",
        RunMethod.Bus => "Cable bus",
        RunMethod.Apo => "Equalizer APO",
        RunMethod.ApoCable => "APO on a cable",
        _ => "Direct",
    };

    /// The fact of sound that is not processed, and of Equalizer APO, which adds no delay.
    public const string NoDelay = "no delay";

    /// The fact of a row whose effects are off while its sound still goes through a cable.
    public const string NoEffects = "no effects";

    /// The short fact under the chip: the delay while the effects run ("70 ms"), "no delay" for Equalizer APO working on its device
    /// (with or without effects) and for sound nothing processes, "ready"; any other state in the row's own short words
    /// (<paramref name="brief"/>: "Starts when it plays", "No free cable"...).
    /// <param name="kind">The status's fact (a running state with no other fact counts as <see cref="StatusKind.Running"/>).</param>
    public static string FactOf(StatusKind kind, RunMethod chip, int? delayMs, string brief) => kind switch
    {
        StatusKind.Running => delayMs is { } d ? $"{d}{DelayUnit}" : chip == RunMethod.Apo ? NoDelay : Brief(StatusKind.Running),
        StatusKind.Ready => Brief(StatusKind.Ready),
        StatusKind.Direct => NoDelay,
        StatusKind.MicrophonesOff => MicrophonesOffFact,
        StatusKind.Other when brief.Length == 0 => chip is RunMethod.None or RunMethod.Apo ? NoDelay : NoEffects,
        _ => brief,
    };
}
