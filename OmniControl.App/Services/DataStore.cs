namespace OmniControl.App.Services;

/// <summary>What a saved file is to backup and wipe.</summary>
public enum StoreKind
{
    /// <summary>The user's settings and lists: exported, imported and wiped.</summary>
    Saved,

    /// <summary>A copy kept for an undo (taken before a "Restore defaults"): wiped, never exported.</summary>
    Undo,

    /// <summary>What lets OmniControl put Windows back as it was (originals, journals): never exported and never wiped, or a change to Windows could no longer be undone.</summary>
    Safety,

    /// <summary>A record OmniControl keeps for itself and the diagnostics (the power journal): wiped, never exported.</summary>
    Record,
}

/// <summary>Where a store sits in "Import settings from another PC": the <see cref="ImportGroups"/> heading, the area's name (stores with the same name are one tick box) and a muted note.</summary>
public sealed record ImportPlace(string Group, string Area, string? Note = null);

/// <summary>The headings of the import window's areas, in order (the sidebar's own groups).</summary>
public static class ImportGroups
{
    public const string Devices = "Devices", Workflow = "Workflow", Keys = "Keys", LookAndApp = "Look and app";

    public static readonly IReadOnlyList<string> All = new[] { Devices, Workflow, Keys, LookAndApp };
}

/// <summary>
/// One json file OmniControl saves under its data folder. Every file is one entry of <see cref="DataStores"/> and is read and written only
/// through <see cref="AppDataStore"/> by its entry, so backup, import and wipe always know every store.
/// </summary>
/// <param name="FileName">The file's name; a name with <c>*</c> is a family of files (one per key, see <see cref="Named"/>).</param>
/// <param name="Area">What the backup window calls it.</param>
/// <param name="Mergeable">An import may merge it (add what is missing, keep what is here) instead of replacing it.</param>
/// <param name="NotImported">Top-level properties an import never takes from a file (the approvals of commands: a file must not pre-approve them).</param>
/// <param name="Import">Where the store sits in the import from another PC; null: that import does not offer it.</param>
/// <param name="MachineSpecific">Belongs to this PC alone (its pairing list, the key its keyboard sends, paths of plug-ins): never sent to another PC.</param>
public sealed record DataStore(string FileName, string Area, StoreKind Kind, bool Mergeable = false, string[]? NotImported = null,
    ImportPlace? Import = null, bool MachineSpecific = false)
{
    public bool IsFamily => FileName.Contains('*');

    /// <summary>The member of a family for <paramref name="key"/> (<c>app</c> in <c>*-before-restore.json</c>).</summary>
    public DataStore Named(string key) => this with { FileName = FileName.Replace("*", key) };
}

/// <summary>
/// The master list of every file OmniControl saves. A new store is one line here (and its class reads it from here), and backup, import
/// and wipe pick it up at once. Logs are not stores.
/// </summary>
public static class DataStores
{
    private static class Places
    {
        public static readonly ImportPlace Displays = new(ImportGroups.Devices, "Displays", "presets");
        public static readonly ImportPlace Volume = new(ImportGroups.Devices, "Volume and effects", "EQ, FX chains");
        public static readonly ImportPlace SmartDevices = new(ImportGroups.Devices, "Smart devices", "no passwords");
        public static readonly ImportPlace Mouse = new(ImportGroups.Devices, "Mouse and trackpad", "buttons, layers");
        public static readonly ImportPlace Gestures = new(ImportGroups.Workflow, "Gestures");
        public static readonly ImportPlace Automation = new(ImportGroups.Workflow, "Automation", "rules, scripts");
        public static readonly ImportPlace Shortcuts = new(ImportGroups.Keys, "Keyboard shortcuts");
        public static readonly ImportPlace Osd = new(ImportGroups.LookAndApp, "OSD", "style, places");
        public static readonly ImportPlace IconsAndLists = new(ImportGroups.LookAndApp, "Icons and app lists");
        public static readonly ImportPlace AppSettings = new(ImportGroups.LookAndApp, "App settings", "theme, start-up");
        public static readonly ImportPlace Confirmations = new(ImportGroups.LookAndApp, "Confirmations", "answers you chose not to be asked again");
    }

    public static readonly DataStore App = new("settings.json", "App settings", StoreKind.Saved, Import: Places.AppSettings);
    public static readonly DataStore Mixer = new("mixer.json", "Volume mixer and wiring", StoreKind.Saved,
        NotImported: new[] { "BusCable", "RealMasterDevice", "CaptureSilence" }, Import: Places.Volume);
    public static readonly DataStore EffectPresets = new("presets.json", "Effect presets", StoreKind.Saved, Mergeable: true, Import: Places.Volume);
    public static readonly DataStore VolumePresets = new("volume-presets.json", "Volume presets", StoreKind.Saved, Mergeable: true, Import: Places.Volume);
    public static readonly DataStore Plugins = new("plugins.json", "Plug-in list", StoreKind.Saved, Mergeable: true, MachineSpecific: true);
    public static readonly DataStore Automation = new("automation.json", "Rules, custom actions and scripts", StoreKind.Saved, Mergeable: true, NotImported: new[] { "Approved" }, Import: Places.Automation);
    public static readonly DataStore GestureRules = new("rules.json", "Gesture rules", StoreKind.Saved, Mergeable: true, NotImported: new[] { "Approved" }, Import: Places.Gestures);
    public static readonly DataStore Gestures = new("gestures.json", "Gestures", StoreKind.Saved, Import: Places.Gestures);
    public static readonly DataStore Hotkeys = new("hotkeys.json", "Shortcuts", StoreKind.Saved, Mergeable: true, Import: Places.Shortcuts);
    public static readonly DataStore HotkeySets = new("hotkey-sets.json", "Shortcut sets", StoreKind.Saved, Mergeable: true, Import: Places.Shortcuts);
    public static readonly DataStore Mouse = new("mouse.json", "Mouse and trackpad", StoreKind.Saved, Import: Places.Mouse);
    public static readonly DataStore Displays = new("display-settings.json", "Displays", StoreKind.Saved, Import: Places.Displays);
    public static readonly DataStore Osd = new("osd.json", "On-screen display", StoreKind.Saved, Import: Places.Osd);
    public static readonly DataStore AppLists = new("app-lists.json", "App lists", StoreKind.Saved, Mergeable: true, Import: Places.IconsAndLists);
    public static readonly DataStore AppActions = new("app-actions.json", "App actions", StoreKind.Saved, Mergeable: true, Import: Places.Automation);
    public static readonly DataStore PowerPlans = new("power-plans.json", "Power plan actions", StoreKind.Saved, Mergeable: true, Import: Places.Automation);
    public static readonly DataStore RecentActions = new("recent-actions.json", "Recent actions", StoreKind.Saved, Mergeable: true, Import: Places.AppSettings);
    public static readonly DataStore Devices = new("devices.json", "Smart devices (never their passwords)", StoreKind.Saved, Mergeable: true, Import: Places.SmartDevices);
    public static readonly DataStore Screenshots = new("screenshots.json", "Screenshot settings", StoreKind.Saved,
        NotImported: new[] { "TurnOffWindowsKeys", "WindowsKeysBackup", "WindowsKeysChangedUtc", "RecordingRoute" },
        Import: Places.AppSettings);
    public static readonly DataStore Tray = new("tray.json", "Tray flyout", StoreKind.Saved, Import: Places.AppSettings);
    public static readonly DataStore Confirmations = new("confirmations.json", "Confirmations you chose not to be asked again", StoreKind.Saved, Import: Places.Confirmations);
    public static readonly DataStore OtherPcs = new("other-pcs.json", "Other PCs (paired PCs and what each last shared; the pairing keys are kept by Windows, never in a backup)", StoreKind.Saved, MachineSpecific: true);
    public static readonly DataStore Icons = new("icons.json", "Your icons and chosen icons", StoreKind.Saved, Import: Places.IconsAndLists);
    public static readonly DataStore MicMuteKey = new("mic-mute-key.json", "Microphone mute key (the key your keyboard sends)", StoreKind.Saved, MachineSpecific: true);

    public static readonly DataStore MixerBeforeRestore = new("mixer-before-restore.json", "Mixer before restore", StoreKind.Undo);
    public static readonly DataStore BeforeRestore = new("*-before-restore.json", "Settings before restore", StoreKind.Undo);

    public static readonly DataStore EngineJournal = new("engine-journal.json", "Effects engine journal", StoreKind.Safety);
    public static readonly DataStore SoftwareDisplay = new("software-display.json", "Display recovery", StoreKind.Safety);
    public static readonly DataStore PointerOriginal = new("pointer-settings-original.json", "Original pointer settings", StoreKind.Safety);
    public static readonly DataStore PointerSwapTrial = new("pointer-swap-trial.json", "Button swap trial", StoreKind.Safety);
    public static readonly DataStore TrackpadOriginal = new("trackpad-settings-original.json", "Original trackpad settings", StoreKind.Safety);

    public static readonly DataStore WindowsOsdHidden = new("windows-osd-hidden.json", "Windows' volume pop-up moved off screen (to put back)", StoreKind.Safety);

    public static readonly DataStore WakeUndo = new("wake-undo.json", "Wake settings OmniControl changed on this PC (to put back)", StoreKind.Safety);

    public static readonly DataStore PowerJournal = new("power-journal.json", "Power journal (sleeps, wakes, log-ins)", StoreKind.Record);

    public static IReadOnlyList<DataStore> All { get; } = new[]
    {
        App, Mixer, EffectPresets, VolumePresets, Plugins, Automation, GestureRules, Gestures, Hotkeys, HotkeySets, Mouse, Displays, Osd,
        AppLists, AppActions, PowerPlans, RecentActions, Devices, Screenshots, Tray, Icons, MicMuteKey, OtherPcs, Confirmations, MixerBeforeRestore, BeforeRestore, EngineJournal, SoftwareDisplay, PointerOriginal,
        PointerSwapTrial, TrackpadOriginal, WindowsOsdHidden, WakeUndo, PowerJournal,
    };

    /// <summary>The stores a backup holds.</summary>
    public static IEnumerable<DataStore> BackedUp => All.Where(s => s.Kind == StoreKind.Saved);

    /// <summary>The backed-up store a file in a backup is, or null when it is not one.</summary>
    public static DataStore? BackedUpByName(string fileName) =>
        BackedUp.FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}
