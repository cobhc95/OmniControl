namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>
/// The "What you will need" box under the goal cards (docs/mockups/easy-cards-multiselect.html): ONE pure function from the ticked goals and
/// the live facts to the lines of the box, in the shape of a checklist line (<see cref="EasyCheck"/>: mark, words, a muted qualifier, a live word
/// at the right, an optional "Get" button). Nothing here reads Windows; the box is worked out again whenever the selection or the facts change,
/// so a line turns done by itself when an install finishes. All its wording lives here.
/// A goal that needs the cable is one whose engine rule refuses Equalizer APO (<see cref="EasyEngineRule.NeedsCable"/>); every other goal
/// can run on Equalizer APO or a cable (the user chooses on the Engine step).
/// </summary>
public static class EasyNeeds
{
    public const string Heading = "What you will need";
    public const string PickAGoal = "Pick a goal to see what it needs.";
    public const string Looking = "Looking at your PC. One moment.";
    public const string GetCablesLabel = "Get virtual cables";

    private const string ApoQualifier = "free, no delay added";
    private const string EitherText = "Equalizer APO, or a virtual cable", EitherQualifier = "you choose on the next step; no cable is needed";
    private const string ApoNotNeeded = "Equalizer APO is not needed";
    private const string MixedNote = "Your master can run on Equalizer APO while a cable does the combining.";

    public static IReadOnlyList<EasyCheck> Of(IReadOnlyCollection<string> selected, EasyFacts f)
    {
        var goals = EasyGoals.All.Where(g => selected.Contains(g.Id)).ToList();
        if (goals.Count == 0) return new[] { Sentence("pick", PickAGoal) };
        if (f.Looking) return new[] { Sentence("looking", Looking) };

        var cableGoals = goals.Where(g => g.Engine is { NeedsCable: true }).ToList();
        var otherGoals = goals.Except(cableGoals).ToList();
        var lines = new List<EasyCheck>();

        if (cableGoals.Count == 0)
        {
            var either = f.ApoInstalled || f.CablesReady > 0;
            lines.Add(new EasyCheck("way", EitherText, EitherQualifier, either ? CheckState.Done : CheckState.Todo, Live: either ? "ready" : "none yet"));
            lines.Add(ApoLine(f));
            return lines;
        }

        if (otherGoals.Count > 0) lines.Add(ApoLine(f));
        lines.Add(CableLine(cableGoals, f));
        if (otherGoals.Count == 0) lines.Add(new EasyCheck("apo-not-needed", ApoNotNeeded, string.Empty, CheckState.Done));
        if (otherGoals.Count > 0) lines.Add(Sentence("mixed", MixedNote));
        if (f.CablesReady < cableGoals.Count) lines.Add(Sentence("shortage", Shortage(f.CablesReady)));
        return lines;
    }

    private static EasyCheck Sentence(string id, string text) => new(id, text, string.Empty, CheckState.Done, Note: true);

    private static EasyCheck ApoLine(EasyFacts f) => new("apo", "Equalizer APO", ApoQualifier, f.ApoInstalled ? CheckState.Done : CheckState.Todo,
        f.ApoInstalled ? null : new EasyFix(EasyFixes.InstallApo, EasyEngines.GetApoLabel), Live: f.ApoInstalled ? "installed" : "not installed");

    /// <summary>"N virtual cables", N = how many selected goals need one pair each; what each is for; how many are ready; a Get button while there are too few.</summary>
    private static EasyCheck CableLine(IReadOnlyList<EasyGoal> cableGoals, EasyFacts f)
    {
        var n = cableGoals.Count;
        var text = n == 1 ? "1 virtual cable" : $"{n} virtual cables";
        var qualifier = n == 1 ? cableGoals[0].CableUse : string.Join(", ", cableGoals.Select(g => $"one {g.CableUse}"));
        var enough = f.CablesReady >= n;
        var live = f.CablesReady > 0 ? $"{f.CablesReady} ready" : f.CableInstalled ? "0 allowed" : "0 installed";
        var fix = enough ? null : new EasyFix(f.CableInstalled ? EasyFixes.MoreCables : EasyFixes.InstallCable, GetCablesLabel);
        return new EasyCheck("cables", text, qualifier, enough ? CheckState.Done : CheckState.Todo, fix, Live: live);
    }

    private static string Shortage(int ready) => ready > 0
        ? $"With your {ready} cable{(ready == 1 ? "" : "s")} you can do some of these now. Next stays on; the setup runs them in order and stops with a clear message where a cable is missing."
        : "None of these can finish until a cable is installed. Next stays on; the setup runs them in order and stops with a clear message where a cable is missing.";
}
