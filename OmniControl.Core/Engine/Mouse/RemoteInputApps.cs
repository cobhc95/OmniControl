namespace OmniControl.Core.Engine.Mouse;

/// THE list of programs whose window passes the keyboard on to another computer (remote desktops, virtual machine consoles, game
/// streaming): while one is in front a key typed with its scan code (<see cref="InputSender.Type"/>) reaches the other computer, where
/// OmniControl's own actions would act on this one.
public static class RemoteInputApps
{
    /// One program: its name for people, its process file names without extension, the classes of its windows that pass keys on when
    /// only some do (null: every window of it), and the shortcuts it may keep on this PC, each with its own replacement that it passes on,
    /// with what says whether the window in front (process id, window) keeps them now (null: it never does).
    public sealed record App(string Name, string[] Processes, string[]? Classes = null, IReadOnlyList<KeySwap>? LocalSwaps = null,
        Func<int, IntPtr, bool>? KeepsKeysHere = null);

    /// A shortcut and the one sent in its place.
    public sealed record KeySwap(string Keys, string SentAs);

    /// What to type into the remote-input window in front: <see cref="Keys"/>, the replacement named <see cref="SentAs"/> when it has one;
    /// <see cref="KeptHere"/>: the window keeps these keys on this PC and has no replacement for them (a Windows-key shortcut).
    public sealed record RemoteKeys(App App, ushort[] Keys, string? SentAs, bool KeptHere);

    // Remote Desktop's own replacements, for a session that keeps the originals on this PC (RdpSession). A Windows-key shortcut has none.
    private static readonly KeySwap[] RdpLocal =
    {
        new("Alt+Tab", "Alt+PageUp"), new("Alt+Shift+Tab", "Alt+PageDown"), new("Alt+Esc", "Alt+Insert"),
        new("Win", "Alt+Home"), new("Ctrl+Esc", "Alt+Home"), new("Ctrl+Alt+Delete", "Ctrl+Alt+End"),
    };

    public static IReadOnlyList<App> All { get; } = new App[]
    {
        // Its connection dialog does not pass keys on; the session window and RemoteApp windows do.
        new("Remote Desktop", new[] { "mstsc" }, new[] { "TscShellContainerClass", "RAIL_WINDOW" }, RdpLocal, RdpSession.KeepsKeysHere),
        new("Windows App and Azure Virtual Desktop", new[] { "msrdc" }),
        new("Hyper-V", new[] { "vmconnect" }),
        new("VMware", new[] { "vmware", "vmplayer", "vmware-vmx" }),
        new("VirtualBox", new[] { "VirtualBoxVM" }),
        new("Citrix Workspace", new[] { "wfica32", "CDViewer" }),
        new("Parsec", new[] { "parsecd" }),
        new("Moonlight", new[] { "Moonlight" }),
        new("TeamViewer", new[] { "TeamViewer" }),
        new("AnyDesk", new[] { "AnyDesk" }),
        new("RustDesk", new[] { "rustdesk" }),
        new("Splashtop", new[] { "strwinclt" }),
        new("NoMachine", new[] { "nxplayer" }),
        new("VNC viewers", new[] { "vncviewer", "tvnviewer" }),
    };

    /// The program of <see cref="All"/> whose window is in front, or null. Reads the window's process path: not for the mouse hook's callback.
    public static App? InFront() => Front().App;

    /// What to type for <paramref name="keys"/> into the remote-input window in front (its replacement where it keeps them on this PC), or
    /// null when none is in front. Reads the process path (and for Remote Desktop its .rdp file): not for the mouse hook's callback.
    public static RemoteKeys? KeysFor(ushort[] keys)
    {
        var (app, window, pid) = Front();
        if (app is null) return null;
        if (app.LocalSwaps is not { } swaps || app.KeepsKeysHere?.Invoke(pid, window) != true) return new RemoteKeys(app, keys, null, false);
        foreach (var swap in swaps)
            if (KeyChord.TryParse(swap.Keys, out var from) && KeyChord.SameKeys(from, keys) && KeyChord.TryParse(swap.SentAs, out var to))
                return new RemoteKeys(app, to, swap.SentAs, false);
        return new RemoteKeys(app, keys, null, KeyChord.HasWinKey(keys));
    }

    private static (App? App, IntPtr Window, int Pid) Front()
    {
        var window = WindowQuery.ActiveWindow();
        if (WindowQuery.ProcessOf(window) is not { } pid || CoreAudio.ProcessPath(pid) is not { } path) return (null, window, 0);
        var process = Path.GetFileNameWithoutExtension(path);
        var app = All.FirstOrDefault(a => a.Processes.Contains(process, StringComparer.OrdinalIgnoreCase));
        return (app?.Classes is not { } classes || classes.Contains(WindowQuery.ClassOf(window), StringComparer.Ordinal) ? app : null, window, pid);
    }
}
