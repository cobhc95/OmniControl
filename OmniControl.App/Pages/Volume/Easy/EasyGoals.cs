using OmniControl.App.Audio;
using OmniControl.App.Pages.Volume.Easy.Flow;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>How a part of a goal card's flow picture is coloured: the method that does the work (green = Equalizer APO, orange = OmniControl's own engine, blue = a virtual cable).</summary>
public enum PictureTone { Plain, Apo, Own, Cable }

/// <summary>One box of a goal card's tiny flow picture ("All apps", "One EQ"); the picture is these in order, joined by arrows.</summary>
public sealed record PicturePart(string Text, PictureTone Tone = PictureTone.Plain);

/// <summary>
/// What the user can set out to do on the Easy page's welcome: its stable id, title, a one-word <paramref name="Short"/> name (the summary
/// after a setup), one fact line and flow picture, and its <see cref="EasyEngineRule"/> (which ways of processing the sound it allows;
/// null = the goal has no Engine step). A goal the cable does (its rule refuses Equalizer APO) says in <paramref name="CableUse"/> what the cable is for
/// ("What you will need").
/// </summary>
public sealed record EasyGoal(string Id, string Title, string Short, string Fact, IReadOnlyList<PicturePart> Picture, EasyEngineRule? Engine = null,
    string CableUse = "");

/// <summary>
/// The master list of goals the welcome (and "Set up again") offers, in the order of its cards (3 + 2; the words of
/// docs/mockups/easy-mode.html and easy-cards-multiselect.html). The cards screen reads only this list; a goal's setup is looked up by its id (<see cref="EasyViewModel"/>).
/// </summary>
public static class EasyGoals
{
    public const string Better = "better", PerApp = "perapp", Microphone = "mic", JoinMicrophones = "joinmics", TwoSpeakers = "twospeakers";

    private static readonly EasyEngineRule BothWays = new(null, null, EasyEngines.ApoFactsDefault, EasyEngines.CableFactsDefault);

    public static readonly IReadOnlyList<EasyGoal> All = new EasyGoal[]
    {
        new(Better, "Make everything sound better", "Sound", "No delay",
            new PicturePart[] { new("All apps"), new("One EQ", PictureTone.Apo), new("Speakers") }, BothWays),
        new(PerApp, "A different sound per app", "Apps", "About 70 ms",
            new PicturePart[] { new("Spotify"), new("Own EQ", PictureTone.Own), new("Speakers") },
            new EasyEngineRule(null, null, "The master runs in Equalizer APO on your speakers. Apps are recorded either way.",
                "The master goes through the cable bus. Apps are recorded either way.")),
        new(Microphone, "Improve my microphone", "Microphone", "Every app hears it",
            new PicturePart[] { new("Mic"), new("Cleanup", PictureTone.Apo), new("Call app") },
            new EasyEngineRule("Microphone effects go through a virtual cable", null, EasyEngines.ApoFactsDefault,
                "The microphone goes through a cable · 30 to 80 ms delay"), "for the microphone"),
        new(JoinMicrophones, "Join two microphones", "Joined microphones", "Needs a free cable",
            new PicturePart[] { new("Mic 1 + Mic 2"), new("Cable", PictureTone.Cable) },
            new EasyEngineRule("Joining microphones needs a virtual cable", null, EasyEngines.ApoFactsDefault, EasyEngines.CableFactsDefault), "to join the microphones"),
        new(TwoSpeakers, "Play on two speakers", "Two speakers", "Needs a cable",
            new PicturePart[] { new("All"), new("Cable", PictureTone.Cable), new("2 speakers") },
            new EasyEngineRule("Several speakers need a virtual cable", null, EasyEngines.ApoFactsDefault, EasyEngines.CableFactsDefault), "for the speakers"),
    };

    /// <summary>
    /// Whether a goal is already on, for the "on" mark on its card, read from the live facts: the master has a sound that is not flat and
    /// its effects are on; any app has its own equaliser on; any microphone has a look on; a combined microphone or combined speaker exists.
    /// </summary>
    public static bool IsOn(string id, EasyFacts facts) => id switch
    {
        Better => facts.MasterFxOn && !EasyEq.IsFlat(facts.MasterGains),
        PerApp => facts.OwnEqPrograms.Count > 0,
        Microphone => facts.OwnEqMicrophones.Count > 0,
        JoinMicrophones => facts.Combined.Any(c => c.Kind == CombinedKind.Microphones),
        TwoSpeakers => facts.Combined.Any(c => c.Kind == CombinedKind.Speakers),
        _ => false,
    };
}
