using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;

namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>
/// The setup checklists, one rule per goal: given what the user chose and what the PC is like now (<see cref="EasyFacts"/>) each
/// returns the lines of its "Setup" screen with their state, and the fix button of a line that can be done. Pure: nothing here reads
/// Windows or changes anything, so the lines tick by themselves whenever the facts are made again and every rule is tested with made-up facts.
/// A step that depends on the lines above it is blocked until they are done; a step that cannot work says why in its subtitle.
/// </summary>
public static class EasyChecks
{
    public const string Waiting = "Finish the steps above first";

    private const string FullControlChange =
        "Full control sends sound through a virtual cable and OmniControl's engine (about 30-80 ms delay). " +
        "It applies to the whole Volume page; switch back in Advanced.";

    private static EasyCheck Line(string id, string title, string subtitle, CheckState state, EasyFix? fix = null, bool optional = false) =>
        new(id, title, subtitle, state, fix, optional);

    // ---- the lines several goals share ----

    /// <summary>"Equalizer APO is installed" with its installer as the fix.</summary>
    public static EasyCheck ApoInstalled(EasyFacts f) => f.ApoInstalled
        ? Line("apo", "Equalizer APO is installed", "It runs the sound inside Windows, with no added delay.", CheckState.Done)
        : Line("apo", "Equalizer APO is not installed", "It runs the sound inside Windows, with no added delay.", CheckState.Todo, new EasyFix(EasyFixes.InstallApo, "Install it"));

    /// <summary>A device Equalizer APO must be switched on for (its Device Selector): done when <paramref name="why"/> is null.</summary>
    private static EasyCheck Covered(EasyFacts f, string id, string? why, string done, string todo, string fixLabel)
    {
        if (why is null) return Line(id, done, "Equalizer APO is on it.", CheckState.Done);
        if (!f.ApoInstalled) return Line(id, todo, "Install Equalizer APO first.", CheckState.Blocked);
        return f.DeviceSelectorFixes
            ? Line(id, todo, $"{why}. Its Device Selector fixes this.", CheckState.Todo, new EasyFix(EasyFixes.ChooseDevices, fixLabel))
            : Line(id, todo, $"{why}.", CheckState.Blocked);
    }

    private static EasyCheck Needs(string id, string title, string reason) => Line(id, title, reason, CheckState.Blocked);

    /// <summary>"Full control is on", with the switch (after a question) as the fix when it can be chosen.</summary>
    private static EasyCheck FullControl(EasyFacts f)
    {
        if (f.FullControl) return Line("full", "Full control is on", "Sound goes through OmniControl's engine.", CheckState.Done);
        return f.FullUnavailable is { } why
            ? Line("full", "Full control is off", $"{why}.", CheckState.Blocked)
            : Line("full", "Full control is off", "Combined speakers need it.", CheckState.Todo,
                new EasyFix(EasyFixes.SwitchToFull, "Switch to Full control", new EasyConfirm("Switch to Full control?", FullControlChange, "Switch")));
    }

    /// <summary>
    /// "A virtual cable is available" for a new combined device: done while one is free; otherwise why not and the next best step: install
    /// VB-CABLE, more cables, or (the master bus holds the only pair) running the master in Equalizer APO instead, when that can be chosen.
    /// </summary>
    private static EasyCheck Cable(EasyCableFact cable, bool microphone)
    {
        const string title = "A virtual cable is available";
        if (cable.FreeName is { } free) return Line("cable", title, $"{free} is free for it.", CheckState.Done);
        if (cable.Missing == StatusKind.NeedsCable)
            return Line("cable", title, "VB-CABLE is not installed.", CheckState.Todo, new EasyFix(EasyFixes.InstallCable, "Install VB-CABLE"));
        if (cable.Missing == StatusKind.CablesOff) return Line("cable", title, $"{SoundModes.CablesOff}. Switch one on under Virtual cables in Advanced.", CheckState.Blocked);
        if (cable.BusName is { } bus)
            return cable.CanFreeBus
                ? Line("cable", title, $"The master uses {bus}. It can run in Equalizer APO instead.", CheckState.Todo,
                    new EasyFix(EasyFixes.FreeCable, "Free the cable", new EasyConfirm("Free the cable?",
                        "The master will run in Equalizer APO instead of the cable bus: one speaker, no VST3 plug-ins. " +
                        "Change it back with Method on the Master row in Advanced.", "Free it")))
                : Line("cable", title, $"The master uses {bus}. {(microphone ? "A microphone" : "Combined speakers")} needs a second cable pair.", CheckState.Todo,
                    new EasyFix(EasyFixes.MoreCables, "More cables"));
        return Line("cable", title, "Every cable is in use.", CheckState.Todo, new EasyFix(EasyFixes.MoreCables, "More cables"));
    }

    private static string AppsList(EasyFacts f, IEnumerable<string> keys) =>
        string.Join(", ", keys.Select(k => f.Programs.FirstOrDefault(p => p.Id == k)?.Name ?? k));

    // ---- card 1: make everything sound better ----

    public static IReadOnlyList<EasyCheck> Better(EasyAnswers a, EasyFacts f)
    {
        var lines = new List<EasyCheck>();
        if (f.FullControl && f.MasterOnBus)
            lines.Add(Line("engine", "OmniControl's engine runs your sound", "Through a virtual cable, with a short delay.", CheckState.Done));
        else
        {
            lines.Add(ApoInstalled(f));
            lines.Add(Covered(f, "speakers", f.SpeakersApoWhy, "Your speakers are selected in Equalizer APO", "Your speakers are not selected yet", "Choose my speakers"));
        }
        lines.Add(ApplySound(f, lines.All(l => l.IsDone)));
        if (!f.FullControl && f.FullUnavailable is null && !lines.Take(2).All(l => l.IsDone))
            lines.Add(Line("other", "Use another way", "Through a virtual cable, with a little delay.", CheckState.Todo,
                new EasyFix(EasyFixes.UseOwnEngine, "Use another way", new EasyConfirm("Use another way?", FullControlChange, "Switch")), optional: true));
        return lines;
    }

    private static EasyCheck ApplySound(EasyFacts f, bool ready)
    {
        const string title = "Apply your sound";
        var chosen = f.MasterStoredGains;
        if (EasyEq.IsFlat(chosen)) return Line("apply", title, "Normal sound: nothing to change.", CheckState.Done);
        var name = EasyEq.PresetOf(chosen)?.Name ?? "My own";
        if (f.MasterFxOn && f.MasterGains.SequenceEqual(chosen)) return Line("apply", title, $"{name} is on for everything you play.", CheckState.Done);
        if (!ready) return Needs("apply", title, Waiting);
        return Line("apply", title, $"Turns {name} on for everything you play.", CheckState.Todo, new EasyFix(EasyFixes.ApplyMasterSound, "Apply my sound"));
    }

    // ---- card 2: a different sound per app ----

    public static IReadOnlyList<EasyCheck> PerApp(EasyAnswers a, EasyFacts f)
    {
        var works = f.ProgramsWhy is null
            ? Line("works", "Effects work for these apps", f.FullControl ? "OmniControl uses a free cable, or records them." : "They are recorded, so no cable is needed.", CheckState.Done)
            : Needs("works", "Effects work for these apps", $"{f.ProgramsWhy}.");
        EasyCheck own;
        if (a.Programs.Count == 0) own = Needs("own", "Give each app its own sound", "Tick at least one app first.");
        else if (a.Programs.All(f.OwnEqPrograms.Contains))
            own = Line("own", "Each app has its own sound", $"{AppsList(f, a.Programs)}: ready to shape from its Sound button.", CheckState.Done);
        else if (!works.IsDone) own = Needs("own", "Give each app its own sound", Waiting);
        else
            own = Line("own", "Give each app its own sound", "Adds a flat equaliser to each.", CheckState.Todo,
                new EasyFix(EasyFixes.ApplyProgramSounds, "Give them their own sound"));
        return new[] { works, own };
    }

    // ---- card 3: improve my microphone ----

    public static IReadOnlyList<EasyCheck> Microphone(EasyAnswers a, EasyFacts f)
    {
        var lines = new List<EasyCheck>();
        if (f.FullControl)
            lines.Add(Line("fast", "Microphone effects run in Fast mode", "In Full control a microphone goes through a cable instead.", CheckState.Todo,
                new EasyFix(EasyFixes.SwitchToFast, "Switch to Fast mode", new EasyConfirm("Switch to Fast mode?",
                    "Fast mode runs master and microphone effects inside Windows and records programs. Combined speakers and playing on several speakers stop until you switch back in Advanced.",
                    "Switch"))));
        lines.Add(ApoInstalled(f));
        if (a.MicrophoneId is not { } id)
        {
            lines.Add(Needs("mic", "Your microphone is selected in Device Selector", "Choose a microphone first."));
            lines.Add(Needs("apply", "Apply", "Choose a microphone first."));
            return lines;
        }
        lines.Add(Covered(f, "mic", f.MicApoWhy.GetValueOrDefault(id), "Your microphone is selected in Device Selector", "Your microphone is not selected yet", "Choose my microphone"));
        var ready = lines.All(l => l.IsDone);
        var gains = a.MicGains;
        var now = f.MicGains.GetValueOrDefault(id) ?? EasyEq.Flat();
        if (now.SequenceEqual(gains))
            lines.Add(Line("apply", "Apply", EasyEq.IsFlat(gains) ? "Normal: nothing to change." : $"{a.MicLook} is on for this microphone (an equaliser cleanup).", CheckState.Done));
        else if (!ready) lines.Add(Needs("apply", "Apply", Waiting));
        else lines.Add(Line("apply", "Apply", $"Turns {a.MicLook} on for this microphone (an equaliser cleanup).", CheckState.Todo, new EasyFix(EasyFixes.ApplyMicrophone, "Apply")));
        return lines;
    }

    // ---- card 4: join two microphones ----

    public static IReadOnlyList<EasyCheck> JoinMicrophones(EasyAnswers a, EasyFacts f)
    {
        var cable = Cable(f.CableForMicrophone, microphone: true);
        const string title = "Create the combined microphone";
        var made = CombinedOf(f, CombinedKind.Microphones, a);
        EasyCheck create;
        if (made is not null) create = Line("create", $"{made.Name} is created", RecordedFrom(made, f.CableForMicrophone), CheckState.Done);
        else if (a.Devices.Count < 2) create = Needs("create", title, "Tick at least two microphones first.");
        else if (!cable.IsDone) create = Needs("create", title, Waiting);
        else create = Line("create", title, $"Mixes {a.Devices.Count} microphones into one.", CheckState.Todo, new EasyFix(EasyFixes.CreateMicrophone, "Create it"));
        return new[] { cable, create };
    }

    // ---- card 5: play on two speakers ----

    public static IReadOnlyList<EasyCheck> TwoSpeakers(EasyAnswers a, EasyFacts f)
    {
        var full = FullControl(f);
        var cable = Cable(f.CableForSpeakers, microphone: false);
        const string title = "Create the combined speaker";
        var made = CombinedOf(f, CombinedKind.Speakers, a);
        EasyCheck create;
        if (made is not null) create = Line("create", $"{made.Name} is created", "Apps can now play on it.", CheckState.Done);
        else if (a.Devices.Count < 2) create = Needs("create", title, "Tick at least two speakers first.");
        else if (!full.IsDone || !cable.IsDone) create = Needs("create", title, Waiting);
        else create = Line("create", title, "Plays the same sound on all of them.", CheckState.Todo, new EasyFix(EasyFixes.CreateSpeakers, "Create it"));
        return new[] { full, cable, create };
    }

    /// <summary>Card 5's last step, shown on its "All set" screen: the master plays on the new device too (only the cable bus can).</summary>
    public static IReadOnlyList<EasyCheck> TwoSpeakersExtras(EasyAnswers a, EasyFacts f)
    {
        const string title = "Everything plays on both speakers";
        if (CombinedOf(f, CombinedKind.Speakers, a) is not { } made) return Array.Empty<EasyCheck>();
        if (f.MasterPlaysOn.Count == 1 && f.MasterPlaysOn[0] == made.Key) return new[] { Line("master", title, $"{made.Name} is where your master plays.", CheckState.Done, optional: true) };
        if (!f.MasterOnBus) return new[] { Line("master", title, $"Choose {made.Name} in an app's Plays on list.", CheckState.Blocked, optional: true) };
        return new[]
        {
            Line("master", title, $"Sends everything you play to {made.Name}.", CheckState.Todo,
                new EasyFix(EasyFixes.PlayOnBoth, "Set my master to play on both", new EasyConfirm("Play everything on both?",
                    $"Sets the master to play on {made.Name}, so every app plays on all its speakers. Change it on the Wiring tab in Advanced.", "Set it")), optional: true),
        };
    }

    // ---- helpers over the answers ----

    /// <summary>The combined device of this kind that has exactly the devices the user ticked, or null.</summary>
    public static EasyCombinedFact? CombinedOf(EasyFacts f, CombinedKind kind, EasyAnswers a) =>
        a.Devices.Count < 2 ? null
        : f.Combined.FirstOrDefault(c => c.Kind == kind && c.Members.Count == a.Devices.Count && c.Members.All(a.Devices.Contains));

    /// <summary>What apps pick as a combined microphone ("Apps record it from CABLE Output"); the cable that is free now when its own is not known yet.</summary>
    public static string RecordedFrom(EasyCombinedFact made, EasyCableFact cable) =>
        (made.RecordingName ?? cable.RecordingName) is { } name ? SoundModes.AppsRecordFrom(name) + "." : "Apps record it from its cable.";

    // ---- the question screens ----

    /// <summary>Why a question screen cannot be left yet (one calm sentence), or null when its answer is enough.</summary>
    public static string? Why(EasyQuestionKind kind, EasyAnswers a, EasyFacts f) => kind switch
    {
        EasyQuestionKind.Programs => a.Programs.Count == 0 ? "Tick at least one app." : null,
        EasyQuestionKind.Microphone => f.Microphones.Count == 0 ? "No microphone was found." : a.MicrophoneId is null ? "Choose a microphone." : null,
        EasyQuestionKind.Microphones => f.Microphones.Count < 2 ? "Fewer than two microphones found."
            : a.Devices.Count < 2 ? "Tick at least two microphones." : null,
        EasyQuestionKind.Speakers => f.Speakers.Count < 2 ? "This PC has only one speaker output."
            : a.Devices.Count < 2 ? "Tick at least two speakers." : null,
        _ => null,
    };
}
