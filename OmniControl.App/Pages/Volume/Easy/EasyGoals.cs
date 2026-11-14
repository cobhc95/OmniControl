using OmniControl.App.Audio;
using OmniControl.App.Pages.Volume.Easy.Flow;

namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>How a part of a goal card's flow picture is coloured: the method that does the work (green = Equalizer APO, orange = OmniControl's own engine, blue = a virtual cable).</summary>
public enum PictureTone { Plain, Apo, Own, Cable }

/// <summary>One box of a goal card's tiny flow picture ("All apps", "One EQ"); the picture is these in order, joined by arrows.</summary>
public sealed record PicturePart(string Text, PictureTone Tone = PictureTone.Plain);

/// <summary>What the user can set out to do on the Easy page's welcome: its stable id, title, one fact line and flow picture.</summary>
public sealed record EasyGoal(string Id, string Title, string Fact, IReadOnlyList<PicturePart> Picture);

/// <summary>
/// The master list of goals the welcome (and "Set up again") offers, in the order of its 3 x 2 cards (the words of
/// docs/mockups/easy-mode.html). The cards screen reads only this list; a goal's setup is looked up by its id (<see cref="EasyViewModel"/>).
/// </summary>
public static class EasyGoals
{
    public const string Better = "better", PerApp = "perapp", Microphone = "mic", JoinMicrophones = "joinmics", TwoSpeakers = "twospeakers", JustVolume = "volume";

    public static readonly IReadOnlyList<EasyGoal> All = new EasyGoal[]
    {
        new(Better, "Make everything sound better", "No delay",
            new PicturePart[] { new("All apps"), new("One EQ", PictureTone.Apo), new("Speakers") }),
        new(PerApp, "A different sound per app", "About 70 ms",
            new PicturePart[] { new("Spotify"), new("Own EQ", PictureTone.Own), new("Speakers") }),
        new(Microphone, "Improve my microphone", "Every app hears it",
            new PicturePart[] { new("Mic"), new("Cleanup", PictureTone.Apo), new("Call app") }),
        new(JoinMicrophones, "Join two microphones", "Needs a free cable",
            new PicturePart[] { new("Mic 1 + Mic 2"), new("Cable", PictureTone.Cable) }),
        new(TwoSpeakers, "Play on two speakers", "Needs a cable",
            new PicturePart[] { new("All"), new("Cable", PictureTone.Cable), new("2 speakers") }),
        new(JustVolume, "Just the volume", "No setup",
            new PicturePart[] { new("Apps"), new("Speakers") }),
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
