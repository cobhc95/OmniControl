using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Pages.Easy.Flow;

/// The kinds of screen a setup goes through: questions to answer, the Engine choice, the setup checklist, the "All set" screen.
public enum EasyStepKind { Question, Engine, Setup, Done }

/// What a question screen asks (what its view shows).
public enum EasyQuestionKind { Sound, Programs, Microphone, MicLook, Microphones, Speakers }

/// One screen of a setup: its words on the progress strip, its kind and, for a question, which.
public sealed record EasyStep(string Title, EasyStepKind Kind, EasyQuestionKind Question = EasyQuestionKind.Sound);

/// The "All set" screen: a headline, a few plain lines and (for a goal that sends the user somewhere) what to do next.
public sealed record EasyDone(string Headline, IReadOnlyList<string> Lines);

/// One goal's whole setup as data: the screens in order (the progress strip is "Goal", these screens' words, "Setup", "Done"; a goal whose
/// <see cref="EasyGoal.Engine"/> rule exists gets the Engine screen between its questions and "Setup"), the
/// checklist of its Setup screen, the optional extra lines of its "All set" screen, and what that screen says. The flow engine
/// (<see cref="EasyFlowViewModel"/>) reads only this; adding a goal means adding an entry to <see cref="EasyFlows.All"/>.
public sealed record EasyFlowDef(
    string GoalId,
    IReadOnlyList<EasyStep> Steps,
    Func<EasyAnswers, EasyFacts, IReadOnlyList<EasyCheck>> Checks,
    Func<EasyAnswers, EasyFacts, EasyDone> Done,
    Func<EasyAnswers, EasyFacts, IReadOnlyList<EasyCheck>>? Extras = null);

/// The master list of setups, one per goal.
public static class EasyFlows
{
    private static readonly EasyStep Setup = new("Setup", EasyStepKind.Setup), Finish = new("Done", EasyStepKind.Done);

    /// The Engine screen; <see cref="EasyFlowViewModel"/> puts it before "Setup" for every goal that has an engine rule.
    public static readonly EasyStep Engine = new("Engine", EasyStepKind.Engine);

    /// The screens a goal goes through: its definition's, with the Engine screen before "Setup" when the goal has an engine rule (unless <paramref name="engine"/> is false: a later goal of a sequence reuses the way already chosen).
    public static IReadOnlyList<EasyStep> StepsOf(EasyGoal goal, EasyFlowDef def, bool engine = true)
    {
        if (goal.Engine is null || !engine) return def.Steps;
        var steps = def.Steps.ToList();
        steps.Insert(steps.FindIndex(s => s.Kind == EasyStepKind.Setup), Engine);
        return steps;
    }

    private static EasyStep Ask(string title, EasyQuestionKind kind) => new(title, EasyStepKind.Question, kind);

    private static IReadOnlyList<string> Lines(params string[] lines) => lines;

    public static readonly IReadOnlyList<EasyFlowDef> All = new EasyFlowDef[]
    {
        new(EasyGoals.Better, new[] { Ask("Sound", EasyQuestionKind.Sound), Setup, Finish }, EasyChecks.Better,
            (a, f) => EasyEq.IsFlat(f.MasterStoredGains)
                ? new EasyDone("All set", Lines("Everything sounds Normal: nothing is changed.", "Pick a sound any time on the Everything row."))
                : new EasyDone("All set", Lines($"Everything you play now sounds like {EasyEq.PresetOf(f.MasterStoredGains)?.Name ?? "your own sound"}.",
                    "Change it any time on the Everything row."))),

        new(EasyGoals.PerApp, new[] { Ask("Apps", EasyQuestionKind.Programs), Setup, Finish }, EasyChecks.PerApp,
            (a, f) => new EasyDone("All set", Lines($"{string.Join(", ", a.Programs.Select(k => f.Programs.FirstOrDefault(p => p.Id == k)?.Name ?? k))}: each has its own sound.",
                "Shape it with Sound on the app's row."))),

        new(EasyGoals.Microphone, new[] { Ask("Microphone", EasyQuestionKind.Microphone), Ask("Look", EasyQuestionKind.MicLook), Setup, Finish }, EasyChecks.Microphone,
            (a, f) => new EasyDone("All set", Lines(
                EasyEq.IsFlat(a.MicGains) ? "Your microphone is left as it was." : $"Your microphone now sounds {a.MicLook} (an equaliser cleanup).",
                "Change it any time with the microphone's Sound button."))),

        new(EasyGoals.JoinMicrophones, new[] { Ask("Microphones", EasyQuestionKind.Microphones), Setup, Finish }, EasyChecks.JoinMicrophones,
            (a, f) => EasyChecks.CombinedOf(f, CombinedKind.Microphones, a) is { } made
                ? new EasyDone("All set", Lines($"{made.Name} is ready.", $"In your call app, pick {made.RecordingName ?? f.CableForMicrophone.RecordingName ?? "the cable's output"} as the microphone."))
                : new EasyDone("All set", Lines("Your combined microphone is ready.", "In your call app, pick the cable's output as the microphone."))),

        new(EasyGoals.TwoSpeakers, new[] { Ask("Speakers", EasyQuestionKind.Speakers), Setup, Finish }, EasyChecks.TwoSpeakers,
            (a, f) => EasyChecks.CombinedOf(f, CombinedKind.Speakers, a) is { } made
                ? new EasyDone("All set", Lines($"{made.Name} is ready.", $"Apps now have \"{made.Name}\" in their Plays on list."))
                : new EasyDone("All set", Lines("Your combined speaker is ready.", "Apps now have it in their Plays on list.")),
            EasyChecks.TwoSpeakersExtras),
    };

    /// The setup of a goal, or null for a goal that has none.
    public static EasyFlowDef? Of(string goalId) => All.FirstOrDefault(f => f.GoalId == goalId);
}
