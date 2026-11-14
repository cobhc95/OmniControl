namespace OmniControl.App.Audio;

/// <summary>A group of rows in the help table (the Sound mode panel's Help): its title and an optional plain note under it.</summary>
public sealed record HelpGroup(string Title, string? Note = null);

/// <summary>
/// One way to set the master and the cables up (a row of the help table, <see cref="SoundModes.HelpCombinations"/>): its number, group,
/// the four columns' words, and what it needs on this PC (how many cable pairs, Equalizer APO on the speakers for the master's effects,
/// Equalizer APO on a cable, a combined device of the user's for the free cable to carry), from which its "Works" column is worked out
/// live (<see cref="SoundModes.WorksOf"/>).
/// </summary>
/// <param name="For">What the free cable carries in this setup when that needs something the user made (a combined microphone or speaker).</param>
public sealed record HelpCombination(
    int Number, HelpGroup Group, string MasterRunsOn, string CableUsedFor, string Programs, string MasterEffects,
    bool MasterLimited, int Cables, bool MasterInApo, bool ApoOnCable = false, CableUse For = CableUse.None);

/// <summary>What choosing a setup sets in one step (<see cref="SoundModes.SetupOf"/>): the mode, the master's method, and whether free cables are switched off to stay free.</summary>
public sealed record SetupChoice(SoundMode Mode, EffectsRoute Master, bool KeepCablesFree);

/// <summary>The live facts the help table's "Works" column and its current row are worked out from (the planner makes it).</summary>
/// <param name="Full">The sound mode is Full control.</param>
/// <param name="Master">How the master runs now (<see cref="RunMethod.Bus"/> or <see cref="RunMethod.Apo"/>).</param>
/// <param name="Pairs">Cable pairs OmniControl may use.</param>
/// <param name="NoCable">Why none can be given out when <paramref name="Pairs"/> is 0.</param>
/// <param name="SpeakersApoWhy">Why Equalizer APO can't run the master on the speakers (null = it can).</param>
/// <param name="MasterHasEffects">The master has effects to run (without any, Equalizer APO needs nothing).</param>
/// <param name="ApoOnACable">Equalizer APO runs on an allowed cable.</param>
/// <param name="RecordableCable">An allowed pair has a recording side apps can record from.</param>
/// <param name="Uses">What the cables are used for now, besides the master bus.</param>
/// <param name="FreedPairs">Cable pairs switched off by setup 5 to stay free: choosing another setup switches them on again.</param>
/// <param name="CombinedMicrophone">The user has made a combined microphone.</param>
/// <param name="CombinedSpeaker">The user has made combined speakers.</param>
public sealed record HelpFacts(bool Full, RunMethod Master, int Pairs, StatusKind NoCable, string? SpeakersApoWhy, bool MasterHasEffects,
    bool ApoOnACable, bool RecordableCable, CableUse Uses, int FreedPairs = 0, bool CombinedMicrophone = false, bool CombinedSpeaker = false)
{
    public static readonly HelpFacts Unknown = new(false, RunMethod.Apo, 0, StatusKind.NeedsCable, null, false, false, false, CableUse.None);
}

/// <summary>
/// A help row's "Works" column: "Available", or the one reason it can't work on this PC now. <paramref name="CanChoose"/> = the row can
/// still be clicked: every row that works, and Fast mode's own row even when its master lacks Equalizer APO (Fast mode can always be
/// chosen: programs are recorded, and its card says what else it lacks).
/// </summary>
public sealed record HelpWorks(string Text, bool Available, bool CanChoose);

/// <summary>
/// The help table of the Sound mode panel (approved mock-up <c>docs/mockups/help-card-table.html</c>, with the owner's later list): every
/// way the master and the cables can be set up, the exact wording of each row, whether it works on this PC now and which row is the
/// current setup. The help card shows only this.
/// </summary>
public static partial class SoundModes
{
    private static readonly HelpGroup OnTheCable = new("Full control, master through the cable");
    private static readonly HelpGroup OffTheCable = new("Full control, master off the cable (the cable is free)",
        "Master on Equalizer APO. With no master effects nothing needs installing. The free cable serves one of these.");
    private static readonly HelpGroup SecondPair = new("Needs a second, independent cable pair");
    private static readonly HelpGroup FastGroup = new("Fast mode (no cable needed)");

    private const string BusMaster = "Cable bus (OmniControl)", ApoMaster = "Equalizer APO on the speakers";
    private const string AllRecorded = "All by recording (+70 ms)";
    private const string AnyEffects = "Any, with VST3; several speakers via + add", ApoEffects = "APO only, no VST3; one speaker";

    /// <summary>Every row of the help table, in its order (the numbers are the owner's).</summary>
    public static IReadOnlyList<HelpCombination> HelpCombinations { get; } = new HelpCombination[]
    {
        new(1, OnTheCable, BusMaster, "The master (a combined device then needs a second pair)", AllRecorded, AnyEffects, false, 1, false),
        new(2, OffTheCable, ApoMaster, "One program", "That one by cable (+8 ms), the rest by recording (+70 ms)", ApoEffects, true, 1, true),
        new(3, OffTheCable, ApoMaster, "A combined microphone (apps record CABLE Output)", AllRecorded, ApoEffects, true, 1, true, For: CableUse.Microphone),
        new(4, OffTheCable, ApoMaster, "A combined speaker (programs that pick it play on all members)", AllRecorded, ApoEffects, true, 1, true,
            For: CableUse.Speaker),
        new(5, OffTheCable, ApoMaster, "Kept free for another app", AllRecorded, ApoEffects, true, 1, true),
        new(6, SecondPair, $"{BusMaster}, on the first cable", "Second cable: a combined microphone, a combined speaker or one program",
            "One program by cable (+8 ms), the rest by recording (+70 ms)", AnyEffects, false, 2, false),
        new(7, SecondPair, ApoMaster, "Cable 1: one program, its EQ done by Equalizer APO on that cable",
            "That one by cable with APO (about +8 ms), the rest by recording", ApoEffects, true, 2, true, ApoOnCable: true),
        new(8, FastGroup, ApoMaster, "Nothing, a combined microphone or another app (speakers: Full control)", AllRecorded, ApoEffects, true, 0, true),
    };

    private const string Available = "Available";

    /// <summary>
    /// Whether a row can work on this PC now: "Available", else the first thing it lacks (a cable, a second pair, Equalizer APO on the
    /// speakers or on a cable, the combined device the free cable would carry). Cables setup 5 switched off count as there: choosing
    /// another setup switches them on again.
    /// </summary>
    public static HelpWorks WorksOf(HelpCombination row, HelpFacts facts)
    {
        var pairs = facts.Pairs + facts.FreedPairs;
        string? missing =
            row.Cables > 0 && pairs == 0 ? Brief(facts.NoCable)
            : pairs < row.Cables ? Capitalised(NeedsSecondCable)
            : row.MasterInApo && facts.MasterHasEffects && facts.SpeakersApoWhy is { } why ? why
            : row.ApoOnCable && !facts.ApoOnACable ? ApoNotOnCable
            : row.For == CableUse.Microphone && !facts.RecordableCable ? "Needs a cable apps can record from"
            : row.For == CableUse.Microphone && !facts.CombinedMicrophone ? "Combine microphones first"
            : row.For == CableUse.Speaker && !facts.CombinedSpeaker ? "Combine speakers first"
            : null;
        return new HelpWorks(missing ?? Available, missing is null, missing is null || row.Number == FastSetup);
    }

    /// <summary>
    /// THE live setup number (the badge, the table's highlight, the setup remembered): Fast mode = 8; the master on the bus alone (1) or
    /// with a second cable in use (6); the master in Equalizer APO with its free cable carrying a program on Equalizer APO (7), a
    /// program (2), a microphone (3), combined speakers (4), or nothing (5).
    /// </summary>
    public static int SetupNumber(HelpFacts facts)
    {
        if (!facts.Full) return FastSetup;
        if (facts.Master == RunMethod.Bus) return facts.Uses == CableUse.None ? 1 : 6;
        return facts.Uses.HasFlag(CableUse.ApoProgram) ? 7
            : facts.Uses.HasFlag(CableUse.Program) ? 2
            : facts.Uses.HasFlag(CableUse.Microphone) ? 3
            : facts.Uses.HasFlag(CableUse.Speaker) ? 4
            : 5;
    }

    /// <summary>
    /// What choosing setup <paramref name="number"/> sets, in one step: the mode, the master's method (Full control) and whether the
    /// cables nothing of the user's holds are switched off so they stay free for another app (setup 5). What the free cable then carries
    /// (a program, a combined microphone or speaker) follows from the rows the user has: a program with effects takes it when it plays.
    /// </summary>
    public static SetupChoice SetupOf(int number) => number switch
    {
        FastSetup => new SetupChoice(Fast, MasterAutomatic, false),
        // Automatic is the cable bus whenever a cable is allowed (these setups need one); with none it falls back to Equalizer APO.
        1 or 6 => new SetupChoice(Full, MasterAutomatic, false),
        5 => new SetupChoice(Full, MasterApo, true),
        _ => new SetupChoice(Full, MasterApo, false),
    };

    /// <summary>Fast mode's own setup number (the last row of the table).</summary>
    public const int FastSetup = 8;

    /// <summary>The setup Full control starts with, the first time and when the remembered one can't work here.</summary>
    public const int DefaultFullSetup = 1;

    /// <summary>
    /// The Full control setup to restore when Full control is chosen: the remembered one (none yet = <see cref="DefaultFullSetup"/>), or,
    /// when it can't work on this PC now, the default, with one calm line that says so (null = nothing to say).
    /// </summary>
    public static (int Number, string? Note) RestoreSetup(int remembered, HelpFacts facts)
    {
        var row = HelpCombinations.FirstOrDefault(r => r.Number == remembered && r.Number != FastSetup);
        if (row is null) return (DefaultFullSetup, null);
        var works = WorksOf(row, facts);
        return works.Available || row.Number == DefaultFullSetup
            ? (row.Number, null)
            : (DefaultFullSetup, $"Setup {row.Number} can't run now ({Lowered(works.Text)}), so Full control uses setup {DefaultFullSetup}.");
    }

    /// <summary>The setup to remember when leaving Full control: the live one, except an unused free cable counts as setup 2 unless it was kept free on purpose.</summary>
    public static int SetupToRemember(int live, bool cablesKeptFree) => live == 5 && !cablesKeptFree ? 2 : live;

    /// <summary>One line naming a setup, for the badge's tooltip.</summary>
    public static string SetupTip(int number) => number switch
    {
        1 => "Setup 1: master on the cable bus.",
        2 => "Setup 2: master on Equalizer APO, the cable used for one program.",
        3 => "Setup 3: master on Equalizer APO, the cable used for a combined microphone.",
        4 => "Setup 4: master on Equalizer APO, the cable used for a combined speaker.",
        5 => "Setup 5: master on Equalizer APO, the cable kept free.",
        6 => "Setup 6: master on the cable bus, a second cable in use.",
        7 => "Setup 7: master on Equalizer APO, a program on a cable with Equalizer APO.",
        _ => "Setup 8: Fast mode, master on Equalizer APO.",
    } + " Click for the table.";

    internal static string Capitalised(string text) => $"{char.ToUpperInvariant(text[0])}{text[1..]}";

    /// <summary>A sentence's first letter in lower case, to follow "Not available: "; a name it starts with keeps its capital.</summary>
    internal static string Lowered(string text) =>
        Names.Any(n => text.StartsWith(n, StringComparison.Ordinal)) ? text : $"{char.ToLowerInvariant(text[0])}{text[1..]}";

    /// <summary>The names a reason can start with (they keep their capital letter).</summary>
    private static readonly string[] Names = { "Equalizer APO", "OmniControl", "Windows", "VB-CABLE" };
}
