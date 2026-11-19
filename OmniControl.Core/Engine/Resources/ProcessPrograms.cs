namespace OmniControl.Core.Engine.Resources;

/// Which program a process id belongs to, as the network readers name it: its identity (<see cref="ProgramIdentity.Normalize"/> of its
/// executable's path, else its process name) and that path. One instance per reader thread (not thread-safe).
internal sealed class ProcessPrograms
{
    private const int QueryLimited = 0x1000;
    private readonly Dictionary<int, (string Id, string Path)> _programs = new();

    public (string Id, string Path) Of(int pid)
    {
        if (_programs.TryGetValue(pid, out var known)) return known;
        var path = PathOf(pid);
        // A fresh name lookup (rare: protected processes), so its own per-id cache never outlives this one's
        return _programs[pid] = (path.Length > 0 ? ProgramIdentity.Normalize(path) : new ProgramIdentity().NameOf(pid), path);
    }

    /// Forgets every process id not in <paramref name="live"/>: once a process has gone, Windows may give its id to another program, which
    /// must be looked up again rather than counted as the old one.
    public void Keep(ICollection<int> live)
    {
        foreach (var pid in _programs.Keys.Where(pid => !live.Contains(pid)).ToList()) _programs.Remove(pid);
    }

    private static string PathOf(int pid)
    {
        var process = OpenProcess(QueryLimited, false, pid);
        if (process == IntPtr.Zero) return string.Empty;
        try
        {
            var text = new StringBuilder(1024);
            var length = text.Capacity;
            return QueryFullProcessImageName(process, 0, text, ref length) ? text.ToString() : string.Empty;
        }
        finally { CloseHandle(process); }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder text, ref int length);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
