namespace OmniControl.App.Features.AppLists.Apps;

/// One app OmniControl can open: its stored id (the action id is "app." + id, never renamed), name, search words and how to find it.
public sealed record AppEntry(string Id, string Title, string Keywords, AppLocator Locator)
{
    public string ActionId => AppCatalogue.ActionPrefix + Id;
}

/// The master list of apps that get an "Open ..." action. Adding an app is one line here: it then shows
/// on the Apps page, the Shortcuts page, the mouse, gesture and rule pickers and in search, but only on a PC where it is installed.
public static class AppCatalogue
{
    public const string ActionPrefix = "app.";

    private static readonly string[] None = Array.Empty<string>();

    private static AppEntry Classic(string id, string title, string keywords, string[] appPaths, string[]? known = null, string[]? startMenu = null) =>
        new(id, title, keywords, new AppLocator
        {
            AppPaths = appPaths,
            KnownPaths = (known ?? None).Select(p => new KnownPath(p)).ToList(),
            StartMenuNames = startMenu ?? new[] { title },
        });

    private static AppEntry Windows(string id, string title, string keywords, string direct) =>
        new(id, title, keywords, new AppLocator { Direct = direct });

    private const string Pf = "%ProgramFiles%", Pf86 = "%ProgramFiles(x86)%", Local = "%LOCALAPPDATA%", Roaming = "%APPDATA%";

    /// Windows' Task Manager (also the taskbar gadget's double-click by default).
    public static readonly AppEntry TaskManager = Windows("taskmanager", "Task Manager", "processes performance", "taskmgr.exe");

    public static readonly IReadOnlyList<AppEntry> All = new[]
    {
        // Music and media
        new AppEntry("spotify", "Spotify", "music streaming player", new AppLocator
        {
            KnownPaths = new[] { new KnownPath(Roaming + @"\Spotify\Spotify.exe") },
            StorePackage = "SpotifyAB.SpotifyMusic", StoreAppId = "Spotify",
        }),
        Classic("amazonmusic", "Amazon Music", "music streaming player", None, new[] { Local + @"\Amazon Music\Amazon Music.exe" }),
        Classic("vlc", "VLC media player", "video player", new[] { "vlc.exe" }, new[] { Pf + @"\VideoLAN\VLC\vlc.exe", Pf86 + @"\VideoLAN\VLC\vlc.exe" }, new[] { "VLC media player" }),
        Classic("obs", "OBS Studio", "record stream broadcast", new[] { "obs64.exe" }, new[] { Pf + @"\obs-studio\bin\64bit\obs64.exe", Pf86 + @"\obs-studio\bin\32bit\obs32.exe" }),

        // Browsers
        Classic("chrome", "Google Chrome", "browser internet web", new[] { "chrome.exe" }, startMenu: new[] { "Google Chrome" }),
        Classic("edge", "Microsoft Edge", "browser internet web", new[] { "msedge.exe" }),
        Classic("firefox", "Firefox", "browser internet web mozilla", new[] { "firefox.exe" }, startMenu: new[] { "Firefox" }),
        Classic("brave", "Brave", "browser internet web", new[] { "brave.exe" }),
        Classic("operagx", "Opera GX", "browser internet web gaming", None, new[] { Local + @"\Programs\Opera GX\opera.exe", Local + @"\Programs\Opera GX\launcher.exe" }, new[] { "Opera GX Browser" }),

        // Chat and meetings
        new AppEntry("discord", "Discord", "chat voice gaming", new AppLocator
        {
            KnownPaths = new[] { new KnownPath(Local + @"\Discord\Update.exe", "--processStart Discord.exe") },
        }),
        new AppEntry("teams", "Microsoft Teams", "chat meeting call", new AppLocator
        {
            StorePackage = "MSTeams", StoreAppId = "MSTeams",
            KnownPaths = new[] { new KnownPath(Local + @"\Microsoft\Teams\Update.exe", "--processStart Teams.exe") },
        }),
        Classic("zoom", "Zoom", "meeting call video", new[] { "Zoom.exe" }, new[] { Pf + @"\Zoom\bin\Zoom.exe", Roaming + @"\Zoom\bin\Zoom.exe", Pf86 + @"\Zoom\bin\Zoom.exe" }, new[] { "Zoom Workplace", "Zoom" }),
        Classic("slack", "Slack", "chat work", new[] { "slack.exe" }, new[] { Local + @"\slack\slack.exe", Pf + @"\Slack\slack.exe" }),
        new AppEntry("whatsapp", "WhatsApp", "chat message", new AppLocator
        {
            StorePackage = "5319275A.WhatsAppDesktop",
            KnownPaths = new[] { new KnownPath(Local + @"\WhatsApp\WhatsApp.exe") },
        }),
        Classic("telegram", "Telegram", "chat message", new[] { "Telegram.exe" }, new[] { Roaming + @"\Telegram Desktop\Telegram.exe" }, new[] { "Telegram" }),

        // Games
        Classic("steam", "Steam", "games store", new[] { "steam.exe" }, new[] { Pf86 + @"\Steam\steam.exe", Pf + @"\Steam\steam.exe" }),
        Classic("epic", "Epic Games Launcher", "games store", None,
            new[] { Pf86 + @"\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe", Pf + @"\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe" },
            new[] { "Epic Games Launcher" }),

        // Work
        Classic("vscode", "Visual Studio Code", "code editor programming", new[] { "Code.exe" }, new[] { Local + @"\Programs\Microsoft VS Code\Code.exe", Pf + @"\Microsoft VS Code\Code.exe" }),
        Classic("notepadpp", "Notepad++", "text editor code", new[] { "notepad++.exe" }, new[] { Pf + @"\Notepad++\notepad++.exe", Pf86 + @"\Notepad++\notepad++.exe" }),
        Classic("word", "Word", "microsoft office document", new[] { "winword.exe" }),
        Classic("excel", "Excel", "microsoft office spreadsheet", new[] { "excel.exe" }),
        Classic("powerpoint", "PowerPoint", "microsoft office presentation slides", new[] { "powerpnt.exe" }),
        Classic("outlook", "Outlook", "microsoft office email mail calendar", new[] { "outlook.exe" }),
        Classic("onenote", "OneNote", "microsoft office notes", new[] { "onenote.exe" }),

        // Windows' own
        Windows("explorer", "File Explorer", "files folders", "explorer.exe"),
        Windows("calculator", "Calculator", "calc maths", "calc.exe"),
        Windows("settings", "Windows Settings", "control panel options", "ms-settings:"),
        TaskManager,
        Windows("notepad", "Notepad", "text editor", "notepad.exe"),
        Windows("paint", "Paint", "draw image", "mspaint.exe"),
        Windows("snippingtool", "Snipping Tool", "screenshot capture", "snippingtool.exe"),
        new AppEntry("terminal", "Windows Terminal", "console command prompt powershell", new AppLocator
        {
            KnownPaths = new[] { new KnownPath(Local + @"\Microsoft\WindowsApps\wt.exe") },
            StorePackage = "Microsoft.WindowsTerminal",
        }),
        Windows("powershell", "PowerShell", "console command terminal", "powershell.exe"),
    };

    /// The entry of an action id ("app.spotify"), or null.
    public static AppEntry? OfAction(string actionId) => All.FirstOrDefault(a => a.ActionId == actionId);
}
