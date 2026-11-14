
namespace OmniControl.App.Services;

public enum ThemeChoice { System, Light, Dark }

/// <summary>Which view of the Volume page the user left open: Easy or Advanced (the Mixer and Wiring tabs). Undecided only until the first start decides it (<see cref="FirstRun"/>).</summary>
public enum VolumeView { Undecided, Easy, Advanced }

/// <summary>User preferences for the app itself, stored as JSON in %APPDATA%\OmniControl.</summary>
public sealed class AppSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>Name of the program-wide accent preset (see AccentService.Presets).</summary>
    public string Accent { get; set; } = "Orange";

    /// <summary>Start with the window hidden in the tray.</summary>
    public bool StartMinimised { get; set; }

    /// <summary>What the window's X button does: ask, hide to the tray, or exit.</summary>
    public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Ask;

    /// <summary>FX chain windows stay above other windows.</summary>
    public bool FxKeepOnTop { get; set; }

    /// <summary>What the equaliser editor shows (the same for every equaliser): its band tabs, the graph's grid, the phase curve.</summary>
    public bool EqShowGrid { get; set; } = true;
    public bool EqShowPhase { get; set; }

    /// <summary>The Volume Mixer lists remembered programs that are not open (off hides them; they keep their settings and return when opened).</summary>
    public bool MixerShowClosed { get; set; } = true;

    /// <summary>The Volume page's view, kept between tabs and runs; the first start decides it (a new install starts in Easy, anyone with settings in Advanced).</summary>
    public VolumeView VolumeView { get; set; } = VolumeView.Undecided;

    /// <summary>The Easy page's welcome (the goal cards) was answered or skipped; it comes back only through "Set up again".</summary>
    public bool EasyWelcomeDone { get; set; }

    /// <summary>How each kind of effect's editor is shown in the FX window (by effect type: a built-in kind, or a plug-in's file).</summary>
    public Dictionary<string, EffectEditorPref> EffectEditors { get; set; } = new();

    /// <summary>The user's own folders the plug-in scan also searches (besides the standard VST3 and VST2 folders), in the order added.</summary>
    public List<string> PluginFolders { get; set; } = new();

    /// <summary>Keys of the collapsible card groups the user expanded (groups start collapsed unless defined otherwise).</summary>
    public List<string> ExpandedGroups { get; set; } = new();

    /// <summary>Keys of groups the user closed on purpose, so a group that starts open stays closed for them.</summary>
    public List<string> CollapsedGroups { get; set; } = new();

    /// <summary>The settings in use by the running program (one shared instance).</summary>
    public static AppSettings Current => Shared.Value;

    /// <summary>Read (and the first start decided) on first use, so merely making an <see cref="AppSettings"/> touches no file.</summary>
    private static readonly Lazy<AppSettings> Shared = new(LoadDecided);

    private const string FileName = "settings.json";

    public static AppSettings Load() => AppDataStore.Load<AppSettings>(FileName) ?? new();

    /// <summary>Loads the settings and, on the first start of this version, records which Volume view to open (saved at once, so a later start cannot see a newer mixer.json and decide again).</summary>
    private static AppSettings LoadDecided()
    {
        var hadFiles = AppDataStore.HasSavedFiles();
        var settings = Load();
        if (FirstRun.Decide(settings, hadFiles)) settings.Save();
        return settings;
    }

    public void Save() => AppDataStore.Save(FileName, this);
}

/// <summary>One effect type's editor view: its scale in percent, and whether the plain list of settings shows instead of the editor.</summary>
public sealed class EffectEditorPref
{
    public int Scale { get; set; }
    public bool Parameters { get; set; }

    /// <summary>A graphic equaliser's sliders reach ±24 dB instead of ±12 (only how far a slider reaches; no gain changes with it).</summary>
    public bool WideRange { get; set; }
}
