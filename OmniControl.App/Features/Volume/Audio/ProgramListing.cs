namespace OmniControl.App.Features.Volume.Audio;

/// One line of the "Add program" menu: a program the user can add, and where OmniControl knows it from.
public sealed record ProgramChoice(string Name, string Path, string Source);

/// Which programs the Volume Mixer lists, and which it offers to add (pure rules, no UI). A program is a row while it is open; while
/// it is closed it is a row only when it is worth remembering (<see cref="AppAudioEntry.KeepsRow"/>) and "Show closed" is on.
/// A closed program that is not a row is offered in the "Add program" menu as recently used, beside the installed programs.
public static class ProgramListing
{
    public const string RecentlyUsedWord = "recently used", InstalledWord = "installed";

    public static bool IsListed(bool isOpen, bool keepsRow, bool showClosed) => isOpen || (showClosed && keepsRow);

    /// The menu's entries: remembered programs that are not rows (recently used) first, then installed ones, each alphabetical. A
    /// program that already has a row, a setup tool of the effects engines (<see cref="IgnoredPrograms"/>) and a second copy of the
    /// same program (by <see cref="AppAudioEntry.KeyFor"/>) are left out. An empty <paramref name="query"/> lists everything.
    public static List<ProgramChoice> Choices(IEnumerable<AppAudioEntry> remembered, IEnumerable<FoundProgram> installed, string query)
    {
        var rows = remembered.Where(e => e.KeepsRow).Select(e => e.Key).ToHashSet();
        var seen = new HashSet<string>(rows);
        var choices = new List<ProgramChoice>();
        Add(choices, seen, remembered.Where(e => !e.KeepsRow && e.ExePath.Length > 0).Select(e => new FoundProgram(e.Name, e.ExePath)), RecentlyUsedWord);
        Add(choices, seen, installed, InstalledWord);
        return choices.Where(c => query.Length == 0 || TextMatch.Any(query, c.Name, System.IO.Path.GetFileName(c.Path))).ToList();
    }

    private static void Add(List<ProgramChoice> into, HashSet<string> seen, IEnumerable<FoundProgram> programs, string source)
    {
        var fresh = programs
            .Select(p => (Program: p, Key: AppAudioEntry.KeyFor(p.Path)))
            .Where(x => !IgnoredPrograms.Contains(x.Key) && seen.Add(x.Key))
            .OrderBy(x => x.Program.Name, StringComparer.CurrentCultureIgnoreCase);
        into.AddRange(fresh.Select(x => new ProgramChoice(x.Program.Name, x.Program.Path, source)));
    }
}
