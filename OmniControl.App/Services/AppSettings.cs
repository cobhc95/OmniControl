
namespace OmniControl.App.Services;

public enum ThemeChoice { System, Light, Dark }

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
    public bool EqShowTabs { get; set; } = true;
    public bool EqShowGrid { get; set; } = true;
    public bool EqShowPhase { get; set; }

    /// <summary>How each kind of effect's editor is shown in the FX window (by effect type: a built-in kind, or a plug-in's file).</summary>
    public Dictionary<string, EffectEditorPref> EffectEditors { get; set; } = new();

    /// <summary>The user's own folders the plug-in scan also searches (besides the standard VST3 and VST2 folders), in the order added.</summary>
    public List<string> PluginFolders { get; set; } = new();

    /// <summary>Keys of the collapsible card groups the user expanded (groups start collapsed unless defined otherwise).</summary>
    public List<string> ExpandedGroups { get; set; } = new();

    /// <summary>Keys of groups the user closed on purpose, so a group that starts open stays closed for them.</summary>
    public List<string> CollapsedGroups { get; set; } = new();

    /// <summary>The settings in use by the running program (one shared instance).</summary>
    public static AppSettings Current { get; } = Load();

    private const string FileName = "settings.json";

    public static AppSettings Load() => AppDataStore.Load<AppSettings>(FileName) ?? new();

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
