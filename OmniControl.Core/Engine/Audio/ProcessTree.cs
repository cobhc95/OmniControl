namespace OmniControl.Core.Engine;

/// Which process to record for a program. Browsers and many apps play sound from a child process of the same .exe, so the capture
/// asks for the topmost process of the same .exe above the one that owns the sound (and its whole tree).
public static class ProcessTree
{
    /// The distinct top processes of the same .exe above each of <paramref name="processIds"/>.
    public static IReadOnlyList<int> Roots(IEnumerable<int> processIds)
    {
        var parents = ParentMap();
        var roots = new HashSet<int>();
        foreach (var pid in processIds)
        {
            var path = CoreAudio.ProcessPath(pid);
            var root = pid;
            var seen = new HashSet<int> { pid }; // process ids are reused: never walk in a circle
            while (parents.TryGetValue(root, out var parent) && seen.Add(parent)
                   && path is not null && string.Equals(CoreAudio.ProcessPath(parent), path, StringComparison.OrdinalIgnoreCase))
                root = parent;
            roots.Add(root);
        }
        return roots.ToList();
    }

    /// Every running process's parent, from one Toolhelp snapshot.
    private static Dictionary<int, int> ParentMap()
    {
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == InvalidHandle) return map;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                map[(int)entry.ProcessId] = (int)entry.ParentProcessId;
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return map;
    }

    /// True while the process is still running. A process that has ended can still be opened while someone holds a handle to it (the
    /// audio service does for its sessions), so its exit code is asked, not only whether it opens.
    public static bool IsRunning(int processId)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return false;
        try
        {
            return GetExitCodeProcess(handle, out var code) && code == StillActive;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint SnapProcess = 0x2;
    private const int QueryLimitedInformation = 0x1000;
    private const uint StillActive = 259;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int access, bool inherit, int processId);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
}
