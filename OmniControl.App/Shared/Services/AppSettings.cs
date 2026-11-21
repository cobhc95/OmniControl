namespace OmniControl.App.Shared.Services;

public enum ThemeChoice { System, Light, Dark }

/// Which view of the Volume page the user left open: Easy or Advanced (the Mixer and Wiring tabs). Undecided only until the first start decides it (<see cref="FirstRun"/>).
public enum VolumeView { Undecided, Easy, Advanced }

/// User preferences for the app itself, stored as JSON in %APPDATA%\OmniControl.
public sealed class AppSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// Name of the program-wide accent choice (see <see cref="AccentService.Names"/>); Windows' own accent by default.
    public string Accent { get; set; } = AccentService.WindowsName;

    /// The user's own accent colour ("#RRGGBB"), kept while another accent choice is in use.
    public string AccentCustom { get; set; } = AccentService.DefaultCustomHex;

    /// How strongly every accent choice is shaded (<see cref="AccentService.Intensity"/>); close to the raw colour by default (a saved 50, the old default, counts as the default at start-up).
    public int AccentIntensity { get; set; } = AccentService.DefaultIntensity;

    /// Name of the background colour choice (see <see cref="SurfaceService.Names"/>); Windows' own tones by default.
    public string Background { get; set; } = SurfaceService.FollowName;

    /// Start with the window hidden in the tray.
    public bool StartMinimised { get; set; }

    /// What the window's X button does: ask, hide to the tray, or exit.
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Ask;

    /// FX chain windows stay above other windows.
    public bool FxKeepOnTop { get; set; }

    /// The hotkeys master switch (<see cref="Features.Hotkeys.HotkeysSwitch"/>): off pauses every keyboard hotkey, keeping its keys.
    public bool HotkeysOn { get; set; } = true;

    /// How often the periodic safety check looks at the hardware (<see cref="HardwareWatches"/>); Off by default: Windows' own announcements already refresh it.
    public HardwareCheck HardwareCheck { get; set; } = HardwareCheck.EveryMinute;

    /// On: Win+A opens the quick panel instead of Windows' quick settings (<see cref="Features.Tray.WinAKey"/>). Off by default.
    public bool WinAForPanel { get; set; }

    /// Width of the FX window's effect list in pixels, as the user dragged it (0 = the standard width).
    public double FxChainListWidth { get; set; }

    /// What the equaliser editor shows (the same for every equaliser): its band tabs, the graph's grid, the phase curve.
    public bool EqShowGrid { get; set; } = true;
    public bool EqShowPhase { get; set; }

    /// The Volume Mixer lists remembered programs that are not open (off hides them; they keep their settings and return when opened).
    public bool MixerShowClosed { get; set; } = true;

    /// The Volume page's view, kept between tabs and runs; the first start decides it (a new install starts in Easy, anyone with settings in Advanced).
    public VolumeView VolumeView { get; set; } = VolumeView.Undecided;

    /// The Easy page's welcome (the goal cards) was answered or skipped; it comes back only through "Set up again".
    public bool EasyWelcomeDone { get; set; }

    /// How each kind of effect's editor is shown in the FX window (by effect type: a built-in kind, or a plug-in's file).
    public Dictionary<string, EffectEditorPref> EffectEditors { get; set; } = new();

    /// The user's own folders the plug-in scan also searches (besides the standard VST3 and VST2 folders), in the order added.
    public List<string> PluginFolders { get; set; } = new();

    /// The update check: whether it runs daily, when it last ran and what it found (see Services/Updates).
    public UpdateSettings Updates { get; set; } = new();

    /// Keys of the collapsible card groups the user expanded (groups start collapsed unless defined otherwise).
    public List<string> ExpandedGroups { get; set; } = new();

    /// Keys of groups the user closed on purpose, so a group that starts open stays closed for them.
    public List<string> CollapsedGroups { get; set; } = new();

    /// How groups start when a page opens (see <see cref="GroupsOpen"/>); the remembered lists above are used only for "Remember last state".
    public GroupsOpenChoice GroupsOpen { get; set; } = GroupsOpenChoice.Remember;

    /// The settings in use by the running program (one shared instance).
    public static AppSettings Current => Shared.Value;

    /// Read (and the first start decided) on first use, so merely making an <see cref="AppSettings"/> touches no file.
    private static readonly Lazy<AppSettings> Shared = new(LoadDecided);

    private static readonly DataStore Store = DataStores.App;

    public static AppSettings Load() => AppDataStore.Load<AppSettings>(Store) ?? new();

    /// Loads the settings and, on the first start of this version, records which Volume view to open (saved at once, so a later start cannot see a newer mixer.json and decide again).
    private static AppSettings LoadDecided()
    {
        var hadFiles = AppDataStore.HasSavedFiles();
        var settings = Load();
        if (FirstRun.Decide(settings, hadFiles)) settings.Save();
        return settings;
    }

    /// Saves off the UI thread a moment later (<see cref="AppDataStore.SaveSoon{T}"/>; a burst of changes is written once).
    public void Save() => AppDataStore.SaveSoon(Store, this);

    /// One field of these settings for the feature that owns it (<see cref="ISettingSlot{T}"/>).
    public ISettingSlot<T> Slot<T>(Func<AppSettings, T> read, Action<AppSettings, T> write) => new SettingSlot<T>(this, read, write);
}

/// What the update check keeps between runs: the daily switch (on by default), the time of the last check and what it found.
public sealed class UpdateSettings
{
    public bool Daily { get; set; } = true;
    public DateTime? LastCheck { get; set; }
    public bool LastFailed { get; set; }

    /// The newest release the last successful check found (whether or not it is newer than this version); null: none yet.
    public ReleaseInfo? Latest { get; set; }
}

/// A release found on GitHub: its version text, its page, when it was published and whether it is marked a pre-release.
public sealed class ReleaseInfo
{
    public string Version { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime? Published { get; set; }
    public bool Prerelease { get; set; }
}

/// One effect type's editor view: its scale in percent, and whether the plain list of settings shows instead of the editor.
public sealed class EffectEditorPref
{
    public int Scale { get; set; }
    public bool Parameters { get; set; }

    /// A graphic equaliser's sliders reach ±24 dB instead of ±12 (only how far a slider reaches; no gain changes with it).
    public bool WideRange { get; set; }
}
