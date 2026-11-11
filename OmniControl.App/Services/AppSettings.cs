
namespace OmniControl.App.Services;

public enum ThemeChoice { System, Light, Dark }

/// <summary>User preferences for the app itself, stored as JSON in %APPDATA%\OmniControl.</summary>
public sealed class AppSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>Name of the program-wide accent preset (see AccentService.Presets).</summary>
    public string Accent { get; set; } = "Orange";

    /// <summary>Start minimised: to the tray when "close to tray" is on, otherwise to the taskbar.</summary>
    public bool StartMinimised { get; set; }

    /// <summary>The window's X button hides it to the tray instead of quitting. On unless the user opts out.</summary>
    public bool CloseToTray { get; set; } = true;


    /// <summary>FX chain windows stay above other windows.</summary>
    public bool FxKeepOnTop { get; set; }

    /// <summary>Keys of the collapsible card groups the user expanded (all groups start collapsed).</summary>
    public List<string> ExpandedGroups { get; set; } = new();

    /// <summary>The settings in use by the running program (one shared instance).</summary>
    public static AppSettings Current { get; } = Load();

    private const string FileName = "settings.json";

    public static AppSettings Load() => AppDataStore.Load<AppSettings>(FileName) ?? new();

    public void Save() => AppDataStore.Save(FileName, this);
}
