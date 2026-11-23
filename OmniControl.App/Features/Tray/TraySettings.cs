namespace OmniControl.App.Features.Tray;

/// One element of the tray flyout or menu, by ids in the master lists only: an action of <see cref="Hotkeys.HotkeyRegistry"/> (a tile or menu
/// item runs it) or a state of <see cref="States.StateRegistry"/> (a slider moves a level state, a tile lights from an on/off state and
/// sets it). An id may hold one <c>*</c>, which stands for any text (<see cref="IdPattern"/>): one element then shows once per match
/// ("every display's brightness"). An element whose ids match nothing is simply not shown.
public sealed class TrayElement
{
    /// The action it runs (a tile, a menu item); null when it is a state's.
    public string? Action { get; set; }

    /// The state it shows and sets (a slider's level, a tile's on/off, a menu item's tick); null for an action alone.
    public string? State { get; set; }

    /// A slider's choice state shown as a pick list in place of its name ("Speakers ▾": the default playback device).
    public string? Choice { get; set; }

    /// A pattern slider shown as ONE row with a display chip ("All displays", then each match) instead of one row per match.
    public bool Together { get; set; }

    /// A level state that moves every output device at once: the speaker row's "All speakers" choice.
    public string? AllState { get; set; }

    /// What the row's chip was set to: a together row's chosen state id (null: all), a speaker row's pinned device name, <see cref="AllPick"/>, or null (it follows Windows' default).
    public string? Pick { get; set; }

    /// The <see cref="Pick"/> of a speaker row that moves every output device.
    public const string AllPick = "*";

    /// The icon the user chose for it (a library id or a user icon, <see cref="Icons.IconLibrary"/>; a section's name saved before the library
    /// still shows that section's icon); null: its own action's or state's icon.
    public string? Icon { get; set; }

    public static TrayElement OfAction(string id, string? state = null) => new() { Action = id, State = state };

    public static TrayElement OfState(string id, string? choice = null) => new() { State = id, Choice = choice };

    public TrayElement Copy() => new() { Action = Action, State = State, Choice = Choice, Together = Together, AllState = AllState, Pick = Pick, Icon = Icon };
}

/// The tray flyout's sliders and tiles, stored in tray.json. Null lists: the defaults (<see cref="TrayDefaults"/>), read from the master lists each time.
public sealed class TraySettings
{
    public List<TrayElement>? Sliders { get; set; }
    public List<TrayElement>? Tiles { get; set; }

    /// "Lock layout" (the tiles' and sliders' right-click menu): while on, nothing can be dragged to another place.
    public bool LockLayout { get; set; }

    /// The tray icon: an icon value (<see cref="Icons.IconLibrary"/> or a user icon); null: the OmniControl logo (a file saved before the
    /// default changed keeps its null, so its logo stays). <see cref="DefaultIcon"/> for a new file and Restore defaults.
    public string? Icon { get; set; } = DefaultIcon;

    /// The tray icon's colour (<see cref="TrayIconChoices.Colours"/>); null: Follow Windows (the default).
    public string? IconColour { get; set; } = DefaultIconColour;

    /// The panel's timings, set on App settings; their defaults and the limits they are clamped to (a file may hold anything) are here.
    public const int ReopenGraceMin = 100, ReopenGraceMax = 1000, RecheckStaggerMin = 100, RecheckStaggerMax = 2000, RecheckGapMin = 1, RecheckGapMax = 60;
    public static readonly int ReopenGraceDefault = (int)Timing.TrayReopenGrace.TotalMilliseconds, RecheckStaggerDefault = (int)Timing.TrayRecheckStagger.TotalMilliseconds,
        RecheckGapDefault = (int)Timing.TrayRecheckMinGap.TotalSeconds;

    /// How long (ms) after the panel hid itself a click on the tray icon only closes it.
    public int ReopenGraceMs { get; set; } = ReopenGraceDefault;

    /// The pause (ms) between one tile's state read and the next when the panel opens.
    public int RecheckStaggerMs { get; set; } = RecheckStaggerDefault;

    /// The least time (s) before a tile's state is read again.
    public int RecheckGapSeconds { get; set; } = RecheckGapDefault;

    /// The tray icon's look by default: the logo outline, white on a dark taskbar and black on a light one.
    public const string DefaultIcon = Icons.IconLibrary.Logo;
    public const string? DefaultIconColour = null;

    private static readonly DataStore Store = DataStores.Tray;

    public static TraySettings Load() => AppDataStore.Load<TraySettings>(Store) ?? new();

    public void Save() => AppDataStore.SaveSoon(Store, this);

    /// The sliders shown: the saved ones, or the defaults.
    public IReadOnlyList<TrayElement> SlidersInUse => Sliders ?? TrayDefaults.Sliders;

    /// The tiles shown: the saved ones, or the defaults.
    public IReadOnlyList<TrayElement> TilesInUse => Tiles ?? TrayDefaults.Tiles;
}
