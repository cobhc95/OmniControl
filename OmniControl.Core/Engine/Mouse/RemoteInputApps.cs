namespace OmniControl.Core.Engine.Mouse;

/// THE list of programs whose window passes the keyboard on to another computer (remote desktops, virtual machine consoles, game
/// streaming): while one is in front a key sent with its scan code (<see cref="InputSender"/>) reaches the other computer, where
/// OmniControl's own actions would act on this one.
public static class RemoteInputApps
{
    /// One program: its name for people, its process file names without extension, and the classes of its windows that pass keys on
    /// when only some do (null: every window of it).
    public sealed record App(string Name, string[] Processes, string[]? Classes = null);

    public static IReadOnlyList<App> All { get; } = new App[]
    {
        // Its connection dialog does not pass keys on; the session window and RemoteApp windows do.
        new("Remote Desktop", new[] { "mstsc" }, new[] { "TscShellContainerClass", "RAIL_WINDOW" }),
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
    public static App? InFront()
    {
        var window = WindowQuery.ActiveWindow();
        if (WindowQuery.ProcessOf(window) is not { } pid || CoreAudio.ProcessPath(pid) is not { } path) return null;
        var process = Path.GetFileNameWithoutExtension(path);
        var app = All.FirstOrDefault(a => a.Processes.Contains(process, StringComparer.OrdinalIgnoreCase));
        return app?.Classes is not { } classes || classes.Contains(WindowQuery.ClassOf(window), StringComparer.Ordinal) ? app : null;
    }
}
