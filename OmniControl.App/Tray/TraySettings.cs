using OmniControl.App.Services;

namespace OmniControl.App.Tray;

/// <summary>
/// One element of the tray flyout or menu, by ids in the master lists only: an action of <see cref="Hotkeys.HotkeyRegistry"/> (a tile or menu
/// item runs it) or a state of <see cref="States.StateRegistry"/> (a slider moves a level state, a tile lights from an on/off state and
/// sets it). An id may hold one <c>*</c>, which stands for any text (<see cref="IdPattern"/>): one element then shows once per match
/// ("every display's brightness"). An element whose ids match nothing is simply not shown.
/// </summary>
public sealed class TrayElement
{
    /// <summary>The action it runs (a tile, a menu item); null when it is a state's.</summary>
    public string? Action { get; set; }

    /// <summary>The state it shows and sets (a slider's level, a tile's on/off, a menu item's tick); null for an action alone.</summary>
    public string? State { get; set; }

    /// <summary>A slider's choice state shown as a pick list in place of its name ("Speakers ▾": the default playback device).</summary>
    public string? Choice { get; set; }

    /// <summary>A pattern slider shown as ONE row with a display chip ("All displays", then each match) instead of one row per match.</summary>
    public bool Together { get; set; }

    /// <summary>A level state that moves every output device at once: the speaker row's "All speakers" choice.</summary>
    public string? AllState { get; set; }

    /// <summary>What the row's chip was set to: a together row's chosen state id (null: all), a speaker row's pinned device name, <see cref="AllPick"/>, or null (it follows Windows' default).</summary>
    public string? Pick { get; set; }

    /// <summary>The <see cref="Pick"/> of a speaker row that moves every output device.</summary>
    public const string AllPick = "*";

    /// <summary>The icon the user chose for it (a library id or a user icon, <see cref="Icons.IconLibrary"/>; a section's name saved before the library
    /// still shows that section's icon); null: its own action's or state's icon.</summary>
    public string? Icon { get; set; }

    public static TrayElement OfAction(string id, string? state = null) => new() { Action = id, State = state };

    public static TrayElement OfState(string id, string? choice = null) => new() { State = id, Choice = choice };

    public TrayElement Copy() => new() { Action = Action, State = State, Choice = Choice, Together = Together, AllState = AllState, Pick = Pick, Icon = Icon };
}

/// <summary>The tray flyout's sliders and tiles, stored in tray.json. Null lists: the defaults (<see cref="TrayDefaults"/>), read from the master lists each time.</summary>
public sealed class TraySettings
{
    public List<TrayElement>? Sliders { get; set; }
    public List<TrayElement>? Tiles { get; set; }

    /// <summary>"Lock layout" (the tiles' and sliders' right-click menu): while on, nothing can be dragged to another place.</summary>
    public bool LockLayout { get; set; }

    private static readonly DataStore Store = DataStores.Tray;

    public static TraySettings Load() => AppDataStore.Load<TraySettings>(Store) ?? new();

    public void Save() => AppDataStore.Save(Store, this);

    /// <summary>The sliders shown: the saved ones, or the defaults.</summary>
    public IReadOnlyList<TrayElement> SlidersInUse => Sliders ?? TrayDefaults.Sliders;

    /// <summary>The tiles shown: the saved ones, or the defaults.</summary>
    public IReadOnlyList<TrayElement> TilesInUse => Tiles ?? TrayDefaults.Tiles;
}
