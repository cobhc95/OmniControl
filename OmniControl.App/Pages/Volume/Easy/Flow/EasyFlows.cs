using OmniControl.App.Audio;

namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>The three kinds of screen a setup goes through: questions to answer, the setup checklist, the "All set" screen.</summary>
public enum EasyStepKind { Question, Setup, Done }

/// <summary>What a question screen asks (what its view shows).</summary>
public enum EasyQuestionKind { Sound, Programs, Microphone, MicLook, Microphones, Speakers }

/// <summary>One screen of a setup: its words on the progress strip, its kind and, for a question, which.</summary>
public sealed record EasyStep(string Title, EasyStepKind Kind, EasyQuestionKind Question = EasyQuestionKind.Sound);

/// <summary>The "All set" screen: a headline, a few plain lines and (for a goal that sends the user somewhere) what to do next.</summary>
public sealed record EasyDone(string Headline, IReadOnlyList<string> Lines);

/// <summary>
/// One goal's whole setup as data: the screens in order (the progress strip is "Goal", these screens' words, "Setup", "Done"), the
/// checklist of its Setup screen, the optional extra lines of its "All set" screen, and what that screen says. The flow engine
/// (<see cref="EasyFlowViewModel"/>) reads only this; adding a goal means adding an entry to <see cref="EasyFlows.All"/>.
/// </summary>
public sealed record EasyFlowDef(
    string GoalId,
    IReadOnlyList<EasyStep> Steps,
    Func<EasyAnswers, EasyFacts, IReadOnlyList<EasyCheck>> Checks,
    Func<EasyAnswers, EasyFacts, EasyDone> Done,
    Func<EasyAnswers, EasyFacts, IReadOnlyList<EasyCheck>>? Extras = null);

/// <summary>The master list of setups, one per goal that has questions or a checklist ("Just the volume" has none: it goes straight to the page).</summary>
public static class EasyFlows
{
    private static readonly EasyStep Setup = new("Setup", EasyStepKind.Setup), Finish = new("Done", EasyStepKind.Done);

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

    /// <summary>The setup of a goal, or null for a goal that has none.</summary>
    public static EasyFlowDef? Of(string goalId) => All.FirstOrDefault(f => f.GoalId == goalId);
}
