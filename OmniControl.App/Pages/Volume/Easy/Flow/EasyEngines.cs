using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;

namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>The two ways the sound can be processed that the Engine step offers: inside Windows by Equalizer APO, or through a virtual cable by OmniControl's own engine.</summary>
public enum EasyEngine { Apo, Cable }

/// <summary>
/// What a goal says about the Engine step (docs/mockups/easy-engine-choice.html): why a way is not offered for it (null = offered) and the
/// one facts line of each card for this goal. A goal with no rule has no Engine step. This is the ONE place that decides which goal allows which way.
/// </summary>
public sealed record EasyEngineRule(string? ApoWhyNot, string? CableWhyNot, string ApoFacts, string CableFacts)
{
    public bool Allows(EasyEngine engine) => WhyNot(engine) is null;

    public string? WhyNot(EasyEngine engine) => engine == EasyEngine.Apo ? ApoWhyNot : CableWhyNot;

    /// <summary>The goal lets the user choose between the two ways (a goal that allows only one has nothing to choose).</summary>
    public bool HasChoice => Allows(EasyEngine.Apo) && Allows(EasyEngine.Cable);

    /// <summary>The cable does this goal (it refuses Equalizer APO): it needs a virtual cable.</summary>
    public bool NeedsCable => !Allows(EasyEngine.Apo);
}

/// <summary>
/// One card of the Engine step as plain data: its words, the state tag (installed or not, from the live facts), whether it can be
/// picked (the goal's rule) and the button that gets it (greyed once it is there).
/// </summary>
public sealed record EasyEngineCard(EasyEngine Engine, string Name, string Description, string Facts, string Tag, bool TagIsGood,
    bool Available, string? Reason, string GetLabel, bool CanGet, EasyFix? Get);

/// <summary>
/// The Engine step's rules, pure: the two cards for a goal and the live facts, which one is pre-selected, and why Next must wait. Nothing
/// here reads Windows; the cards tick over by themselves whenever the facts are made again (an install finishing turns its tag good).
/// </summary>
public static class EasyEngines
{
    public const string Prompt = "How would you like the sound to be processed?";
    public const string Hint = "Both give you the same sound. They differ in what they need.";

    /// <summary>The facts line most goals use for each way.</summary>
    public const string ApoFactsDefault = "No delay added · one speaker at a time · a free tool";
    public const string CableFactsDefault = "30 to 80 ms delay · several speakers · any plug-in";

    private const string ApoName = "Equalizer APO", CableName = "Virtual audio device (wire)";

    /// <summary>The words of the buttons that get each way (the cards and the "What you will need" lines).</summary>
    public const string GetApoLabel = "Get Equalizer APO", GetCableLabel = "Get a virtual cable";

    public static IReadOnlyList<EasyEngineCard> Cards(EasyEngineRule rule, EasyFacts f) => new[] { ApoCard(rule, f), CableCard(rule, f) };

    private static EasyEngineCard ApoCard(EasyEngineRule rule, EasyFacts f)
    {
        var available = rule.Allows(EasyEngine.Apo);
        const string label = GetApoLabel;
        return new EasyEngineCard(EasyEngine.Apo, ApoName, "Changes your sound right on your speakers. No virtual driver needed.", rule.ApoFacts,
            f.ApoInstalled ? "Installed" : "Not installed", f.ApoInstalled, available, rule.ApoWhyNot, label, available && !f.ApoInstalled,
            new EasyFix(EasyFixes.InstallApo, label));
    }

    private static EasyEngineCard CableCard(EasyEngineRule rule, EasyFacts f)
    {
        var available = rule.Allows(EasyEngine.Cable);
        const string label = GetCableLabel;
        var tag = f.CablesReady > 0 ? (f.CablesReady == 1 ? "1 cable ready" : $"{f.CablesReady} cables ready")
            : f.CableInstalled ? EffectsRules.CablesOff : "No virtual cable installed";
        return new EasyEngineCard(EasyEngine.Cable, CableName, "OmniControl takes your sound through a virtual cable, changes it, and plays it on your speakers.",
            rule.CableFacts, tag, f.CablesReady > 0, available, rule.CableWhyNot, label, available && !f.CableInstalled,
            new EasyFix(EasyFixes.InstallCable, label));
    }

    /// <summary>Whether the way is there to use now: Equalizer APO installed, or at least one virtual cable allowed.</summary>
    public static bool IsReady(EasyEngine engine, EasyFacts f) => engine == EasyEngine.Apo ? f.ApoInstalled : f.CablesReady > 0;

    /// <summary>
    /// The way chosen when the user has not picked one: the only one the goal offers, else Equalizer APO unless only a cable is installed
    /// (nothing installed also gives Equalizer APO, the simpler one to get).
    /// </summary>
    public static EasyEngine Default(EasyEngineRule rule, EasyFacts f)
    {
        if (!rule.Allows(EasyEngine.Apo)) return EasyEngine.Cable;
        if (!rule.Allows(EasyEngine.Cable)) return EasyEngine.Apo;
        return !f.ApoInstalled && f.CablesReady > 0 ? EasyEngine.Cable : EasyEngine.Apo;
    }

    /// <summary>The chosen way if the goal offers it, else the default one.</summary>
    public static EasyEngine Resolve(EasyEngine? chosen, EasyEngineRule rule, EasyFacts f) =>
        chosen is { } engine && rule.Allows(engine) ? engine : Default(rule, f);

    /// <summary>Why Next must wait on the Engine step (one plain sentence), or null when the chosen way is installed.</summary>
    public static string? NextWhy(EasyEngine engine, EasyFacts f)
    {
        if (IsReady(engine, f)) return null;
        if (engine == EasyEngine.Apo) return "Install Equalizer APO first.";
        return f.CableInstalled ? $"{EffectsRules.CablesOff}. Switch one on under Virtual cables in Advanced." : "Install a virtual cable first.";
    }
}
