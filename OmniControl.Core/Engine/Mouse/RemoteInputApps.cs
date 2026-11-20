namespace OmniControl.Core.Engine.Mouse;

/// THE list of programs whose window passes the keyboard on to another computer (remote desktops, virtual machine consoles, game
/// streaming): while one is in front a key sent with its scan code (<see cref="InputSender"/>) reaches the other computer, where
/// OmniControl's own actions would act on this one.
public static class RemoteInputApps
{
    /// One program: its name for people, its process file names without extension, the classes of its windows that pass keys on when
    /// only some do (null: every window of it), and the shortcuts it keeps on this PC while its window is not full screen, each with its
    /// own replacement that it passes on (null: none).
    public sealed record App(string Name, string[] Processes, string[]? Classes = null, IReadOnlyList<KeySwap>? WindowedSwaps = null);

    /// A shortcut and the one sent in its place.
    public sealed record KeySwap(string Keys, string SentAs);

    /// What to send to the remote-input window in front: <see cref="Keys"/>, the replacement named <see cref="SentAs"/>; without one, the
    /// keys hold the Windows key, which a windowed session may keep on this PC.
    public sealed record RemoteKeys(App App, ushort[] Keys, string? SentAs);

    // Remote Desktop's own replacements, for a session that is not full screen and whose "Apply Windows key combinations" is the default
    // (full screen only): Windows keeps the originals on this PC. A Windows-key shortcut has none.
    private static readonly KeySwap[] RdpWindowed =
    {
        new("Alt+Tab", "Alt+PageUp"), new("Alt+Shift+Tab", "Alt+PageDown"), new("Alt+Esc", "Alt+Insert"),
        new("Win", "Alt+Home"), new("Ctrl+Esc", "Alt+Home"), new("Ctrl+Alt+Delete", "Ctrl+Alt+End"),
    };

    public static IReadOnlyList<App> All { get; } = new App[]
    {
        // Its connection dialog does not pass keys on; the session window and RemoteApp windows do.
        new("Remote Desktop", new[] { "mstsc" }, new[] { "TscShellContainerClass", "RAIL_WINDOW" }, RdpWindowed),
        new("Windows App and Azure Virtual Desktop", new[] { "msrdc" }, WindowedSwaps: RdpWindowed),
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

    /// For keys about to be sent: what to send instead to a remote-input window in front that is not full screen and keeps them on this PC
    /// (its replacement), or the same keys not swapped when they hold the Windows key and it has none; null otherwise. Not for the mouse hook's callback.
    public static RemoteKeys? WindowedKeysFor(ushort[] keys)
    {
        var (app, window) = Front();
        if (app?.WindowedSwaps is not { } swaps || WindowQuery.CoversItsMonitor(window)) return null;
        foreach (var swap in swaps)
            if (KeyChord.TryParse(swap.Keys, out var from) && KeyChord.SameKeys(from, keys) && KeyChord.TryParse(swap.SentAs, out var to))
                return new RemoteKeys(app, to, swap.SentAs);
        return KeyChord.HasWinKey(keys) ? new RemoteKeys(app, keys, null) : null;
    }

    private static (App? App, IntPtr Window) Front()
    {
        var window = WindowQuery.ActiveWindow();
        if (WindowQuery.ProcessOf(window) is not { } pid || CoreAudio.ProcessPath(pid) is not { } path) return (null, window);
        var process = Path.GetFileNameWithoutExtension(path);
        var app = All.FirstOrDefault(a => a.Processes.Contains(process, StringComparer.OrdinalIgnoreCase));
        return (app?.Classes is not { } classes || classes.Contains(WindowQuery.ClassOf(window), StringComparer.Ordinal) ? app : null, window);
    }
}
