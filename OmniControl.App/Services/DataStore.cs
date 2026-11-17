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
}

/// <summary>
/// One json file OmniControl saves under its data folder. Every file is one entry of <see cref="DataStores"/> and is read and written only
/// through <see cref="AppDataStore"/> by its entry, so backup, import and wipe always know every store.
/// </summary>
/// <param name="FileName">The file's name; a name with <c>*</c> is a family of files (one per key, see <see cref="Named"/>).</param>
/// <param name="Area">What the backup window calls it.</param>
/// <param name="Mergeable">An import may merge it (add what is missing, keep what is here) instead of replacing it.</param>
/// <param name="NotImported">Top-level properties an import never takes from a file (the approvals of commands: a file must not pre-approve them).</param>
public sealed record DataStore(string FileName, string Area, StoreKind Kind, bool Mergeable = false, string[]? NotImported = null)
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
    public static readonly DataStore App = new("settings.json", "App settings", StoreKind.Saved);
    public static readonly DataStore Mixer = new("mixer.json", "Volume mixer and wiring", StoreKind.Saved);
    public static readonly DataStore EffectPresets = new("presets.json", "Effect presets", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore VolumePresets = new("volume-presets.json", "Volume presets", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore Plugins = new("plugins.json", "Plug-in list", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore Automation = new("automation.json", "Rules, custom actions and scripts", StoreKind.Saved, Mergeable: true, NotImported: new[] { "Approved" });
    public static readonly DataStore GestureRules = new("rules.json", "Gesture rules", StoreKind.Saved, Mergeable: true, NotImported: new[] { "Approved" });
    public static readonly DataStore Gestures = new("gestures.json", "Gestures", StoreKind.Saved);
    public static readonly DataStore Hotkeys = new("hotkeys.json", "Shortcuts", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore HotkeySets = new("hotkey-sets.json", "Shortcut sets", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore Mouse = new("mouse.json", "Mouse and trackpad", StoreKind.Saved);
    public static readonly DataStore Displays = new("display-settings.json", "Displays", StoreKind.Saved);
    public static readonly DataStore Osd = new("osd.json", "On-screen display", StoreKind.Saved);
    public static readonly DataStore AppLists = new("app-lists.json", "App lists", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore AppActions = new("app-actions.json", "App actions", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore PowerPlans = new("power-plans.json", "Power plan actions", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore RecentActions = new("recent-actions.json", "Recent actions", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore Devices = new("devices.json", "Smart devices (never their passwords)", StoreKind.Saved, Mergeable: true);
    public static readonly DataStore Screenshots = new("screenshots.json", "Screenshot settings", StoreKind.Saved);
    public static readonly DataStore Tray = new("tray.json", "Tray flyout", StoreKind.Saved);
    public static readonly DataStore Confirmations = new("confirmations.json", "Confirmations you chose not to be asked again", StoreKind.Saved);
    public static readonly DataStore OtherPcs = new("other-pcs.json", "Other PCs (paired PCs and what each last shared; the pairing keys are kept by Windows, never in a backup)", StoreKind.Saved);
    public static readonly DataStore Icons = new("icons.json", "Your icons and chosen icons", StoreKind.Saved);
    public static readonly DataStore MicMuteKey = new("mic-mute-key.json", "Microphone mute key (the key your keyboard sends)", StoreKind.Saved);

    public static readonly DataStore MixerBeforeRestore = new("mixer-before-restore.json", "Mixer before restore", StoreKind.Undo);
    public static readonly DataStore BeforeRestore = new("*-before-restore.json", "Settings before restore", StoreKind.Undo);

    public static readonly DataStore EngineJournal = new("engine-journal.json", "Effects engine journal", StoreKind.Safety);
    public static readonly DataStore SoftwareDisplay = new("software-display.json", "Display recovery", StoreKind.Safety);
    public static readonly DataStore PointerOriginal = new("pointer-settings-original.json", "Original pointer settings", StoreKind.Safety);
    public static readonly DataStore PointerSwapTrial = new("pointer-swap-trial.json", "Button swap trial", StoreKind.Safety);
    public static readonly DataStore TrackpadOriginal = new("trackpad-settings-original.json", "Original trackpad settings", StoreKind.Safety);

    public static IReadOnlyList<DataStore> All { get; } = new[]
    {
        App, Mixer, EffectPresets, VolumePresets, Plugins, Automation, GestureRules, Gestures, Hotkeys, HotkeySets, Mouse, Displays, Osd,
        AppLists, AppActions, PowerPlans, RecentActions, Devices, Screenshots, Tray, Icons, MicMuteKey, OtherPcs, Confirmations, MixerBeforeRestore, BeforeRestore, EngineJournal, SoftwareDisplay, PointerOriginal,
        PointerSwapTrial, TrackpadOriginal,
    };

    /// <summary>The stores a backup holds.</summary>
    public static IEnumerable<DataStore> BackedUp => All.Where(s => s.Kind == StoreKind.Saved);

    /// <summary>The backed-up store a file in a backup is, or null when it is not one.</summary>
    public static DataStore? BackedUpByName(string fileName) =>
        BackedUp.FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}
