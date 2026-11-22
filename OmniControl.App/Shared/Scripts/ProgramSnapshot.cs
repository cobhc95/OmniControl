using OmniControl.Core.Engine;
using OmniControl.App.Shared.Services;

namespace OmniControl.App.Shared.Scripts;

/// A program a picker offers: its identity (<see cref="ProgramNames.Normalize"/>), the name a person knows it by, its .exe when known, and
/// whether it has a window now (otherwise it was found installed or remembered).
public sealed record ProgramFound(string Id, string Name, string? Path, bool HasWindow);

/// The programs a picker offers, read ONCE off the UI thread when it opens: the running ones, the remembered ones and the installed ones,
/// each once, by name, and which programs are running at that moment. It never changes afterwards (a list that followed the running
/// processes kept moving under the pointer); <see cref="Ready"/> is raised on the UI thread when the one read finished.
public sealed class ProgramSnapshot
{
    private volatile IReadOnlyList<ProgramFound> _found = Array.Empty<ProgramFound>();
    private volatile IReadOnlySet<string> _running = new HashSet<string>();

    /// <param name="remembered">The remembered program paths, taken on the UI thread by the caller.</param>
    /// <param name="runningNow">Lists the identities of every running process (the one list the rules' look reads), so the two agree.</param>
    public ProgramSnapshot(IReadOnlyList<string> remembered, Func<IReadOnlySet<string>> runningNow) =>
        Guard.OffUi("reading the programs to offer", () =>
        {
            _found = Merge(ProgramIdentity.Running(), remembered, InstalledPrograms.Find());
            _running = runningNow();
            Dispatcher.UIThread.Post(() => Ready?.Invoke());
        });

    /// The programs read, each once, windowed ones first (empty until <see cref="Ready"/>).
    public IReadOnlyList<ProgramFound> Found => _found;

    /// The identities of the programs running when the read was made (empty until <see cref="Ready"/>).
    public IReadOnlySet<string> Running => _running;

    public event Action? Ready;

    /// The programs with a window, then the remembered and the installed ones, each program once.
    private static List<ProgramFound> Merge(IReadOnlyList<RunningProgram> windowed, IReadOnlyList<string> remembered, IReadOnlyList<FoundProgram> installed)
    {
        var found = windowed.Select(p => new ProgramFound(p.Id, p.Name, p.Path, true)).ToList();
        var seen = found.Select(p => p.Id).ToHashSet();
        void Add(string path, string name)
        {
            var id = ProgramNames.Normalize(path);
            if (id.Length > 0 && seen.Add(id)) found.Add(new ProgramFound(id, name, path, false));
        }
        foreach (var path in remembered.Where(p => System.IO.Path.IsPathRooted(p))) Add(path, ProgramFile.NameOf(path));
        foreach (var program in installed) Add(program.Path, program.Name);
        return found;
    }
}
