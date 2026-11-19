using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.AppLists;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// Mouse remapping preferences, stored as JSON in %APPDATA%\OmniControl\mouse.json. Remapping is always on: an input at Default keeps
/// Windows' behaviour, and Windows' own gesture is turned off for every trackpad gesture that has an action which does not say "also
/// let Windows do its own gesture" (<see cref="ReplacesWindows"/>, <c>TrackpadService</c>). Values of removed switches ("Enabled",
/// "TakeOverWindowsGestures") in an older file are ignored.
public sealed class MouseSettings
{
    public const int Layers = 10;

    /// The action per layer and input, keyed "layer.Button" (for example "1.Middle", "2.ThreeFingerUp"). The layer number belongs to the
    /// input's family (<see cref="InputFamilies.FamilyOf"/>): "2.Middle" is on mouse layer 2, "2.ThreeFingerUp" on trackpad layer 2.
    /// A missing key leaves the input as it is. Holds the input's first action; any others are in <see cref="ExtraActions"/>.
    public Dictionary<string, string> Bindings { get; set; } = new();

    /// The second and later actions of an input, keyed like <see cref="Bindings"/> (which keeps the first, so a file saved before inputs
    /// had several actions loads unchanged and an older version still reads the first). Kept only while the input has a first action
    /// of its own (<see cref="SetActions"/>); each entry is the same stored text as a binding.
    public Dictionary<string, List<string>> ExtraActions { get; set; } = new();

    /// The most actions one input can have on one layer.
    public const int MaxActions = 20;

    /// Names the user gave to mouse layers (layer number to name); a layer without one is called "Layer N".
    public Dictionary<int, string> LayerNames { get; set; } = new();

    /// True: the mouse Layer drop-down lists only layer 1, named layers and layers with mappings.
    public bool OnlyUsedLayers { get; set; } = true;

    /// Names the user gave to trackpad layers. Null only in a file saved before the trackpad had its own layers (<see cref="Upgrade"/>).
    public Dictionary<int, string>? TrackpadLayerNames { get; set; }

    /// True once <see cref="Upgrade"/> has removed trackpad names copied from the mouse's; it then never looks again.
    public bool TrackpadNamesIndependent { get; set; }

    /// True: the trackpad Layer drop-down lists only layer 1, named layers and layers with mappings.
    public bool TrackpadOnlyUsedLayers { get; set; } = true;

    /// The apps the Mouse Buttons group works in (null: every app).
    public AppScopeSetting? ButtonsScope { get; set; }

    /// The apps the Trackpad group's gestures work in (null: every app).
    public AppScopeSetting? TrackpadScope { get; set; }

    /// An input's own apps scope, by its name ("Back", "ThreeFingerUp"); an input without an entry follows its group's.
    public Dictionary<string, AppScopeSetting> CardScopes { get; set; } = new();

    /// True: a middle press no action uses is swallowed unless it is over a link, tab, button or menu item, so an app's own middle-click
    /// autoscroll never starts; and "Change movement to scroll" holds its button back (see <c>MouseService</c>). One setting for every button.
    public bool BlockAutoscroll { get; set; } = true;

    /// True: while a full-screen app (a game) is in front, every mouse input passes untouched (buttons, gestures, the autoscroll block).
    public bool PauseInFullScreen { get; set; }

    /// The apps mouse features never act in ("all except" a list); null: no exclusions.
    public AppScopeSetting? NeverActIn { get; set; }

    /// True: OmniControl starts as administrator (Windows asks at each start), so mouse features also work over programs run as administrator.
    public bool RunAsAdministrator { get; set; }

    /// Puts the "Where mouse features work" group's settings back to their defaults.
    public void RestoreWhereDefaults()
    {
        PauseInFullScreen = false;
        NeverActIn = null;
        RunAsAdministrator = false;
    }

    private static readonly DataStore Store = DataStores.Mouse;

    public static MouseSettings Load()
    {
        var settings = AppDataStore.Load<MouseSettings>(Store) ?? new();
        settings.Upgrade();
        return settings;
    }

    /// Brings a file from an older version up to date. Shared layers (before the trackpad had its own): every gesture mapping keeps its
    /// layer number, so layer N's gestures are now trackpad layer N's; the trackpad's names start empty (never copied from the mouse's);
    /// and a gesture's layer action now moves the trackpad's layer.
    internal void Upgrade()
    {
        ExtraActions ??= new(); // a hand-edited file may say null
        CardScopes ??= new();
        if (TrackpadLayerNames is null)
        {
            TrackpadLayerNames = new();
            foreach (var (key, spec) in Bindings.ToList())
            {
                if (ParseKey(key) is not { } parsed || parsed.Button.FamilyOf() != InputFamily.Trackpad) continue;
                Bindings[key] = MouseActionCatalog.MoveLayerActionTo(InputFamily.Trackpad, spec);
            }
        }
        if (TrackpadNamesIndependent) return;
        // An earlier version copied the mouse's names onto the trackpad; a trackpad name equal to the mouse's of the same number is taken
        // for such a copy and dropped (a name typed by the user that happens to match is lost with it).
        foreach (var (layer, name) in TrackpadLayerNames.ToList())
            if (LayerNames.TryGetValue(layer, out var mouseName) && mouseName == name) TrackpadLayerNames.Remove(layer);
        TrackpadNamesIndependent = true;
    }

    public void Save() => AppDataStore.Save(Store, this);

    /// What a new install has: no mappings, no layer names, every option at its default (upgraded the way a loaded file is, so the two compare equal).
    public static MouseSettings Defaults()
    {
        var settings = new MouseSettings();
        settings.Upgrade();
        return settings;
    }

    /// Takes every value of <paramref name="other"/> (this object stays, so whatever holds it sees the change).
    public void CopyFrom(MouseSettings other)
    {
        Bindings = other.Bindings;
        ExtraActions = other.ExtraActions;
        LayerNames = other.LayerNames;
        OnlyUsedLayers = other.OnlyUsedLayers;
        TrackpadLayerNames = other.TrackpadLayerNames;
        TrackpadNamesIndependent = other.TrackpadNamesIndependent;
        TrackpadOnlyUsedLayers = other.TrackpadOnlyUsedLayers;
        ButtonsScope = other.ButtonsScope;
        TrackpadScope = other.TrackpadScope;
        CardScopes = other.CardScopes;
        BlockAutoscroll = other.BlockAutoscroll;
        PauseInFullScreen = other.PauseInFullScreen;
        NeverActIn = other.NeverActIn;
        RunAsAdministrator = other.RunAsAdministrator;
    }

    /// The apps scope in force for a mouse button or trackpad gesture: its own, else its group's; null when it works in every app.
    public AppScopeSetting? ScopeOf(MouseButton button)
    {
        var scope = CardScopes.GetValueOrDefault(button.ToString()) ?? (button.FamilyOf() == InputFamily.Trackpad ? TrackpadScope : ButtonsScope);
        return scope is { IsLimited: true } ? scope : null;
    }

    /// Stores a button's own scope (null: it follows the group).
    public void SetCardScope(MouseButton button, AppScopeSetting? scope)
    {
        if (scope is null) CardScopes.Remove(button.ToString());
        else CardScopes[button.ToString()] = scope;
    }

    /// One input family's layers: names, the "only used" choice, counts and clearing.
    public LayerSet LayersOf(InputFamily family) => new(this, family);

    public static string KeyOf(int layer, MouseButton button) => $"{layer}.{button}";

    /// The layer and input a stored key names, or null for a key this version can't read.
    public static (int Layer, MouseButton Button)? ParseKey(string key)
    {
        var dot = key.IndexOf('.');
        return dot > 0 && int.TryParse(key.AsSpan(0, dot), out var layer) && Enum.TryParse<MouseButton>(key.AsSpan(dot + 1), out var button)
            ? (layer, button)
            : null;
    }

    /// The input's first action on <paramref name="layer"/>, or null when it has none (Default).
    public string? Get(int layer, MouseButton button) => Bindings.GetValueOrDefault(KeyOf(layer, button));

    /// Every action of the input on <paramref name="layer"/>, in the order they run; empty when it has none (Default).
    public IReadOnlyList<string> ActionsOf(int layer, MouseButton button)
    {
        var key = KeyOf(layer, button);
        if (!Bindings.TryGetValue(key, out var first)) return Array.Empty<string>();
        return ExtraActions.TryGetValue(key, out var extras) ? extras.Prepend(first).ToList() : new[] { first };
    }

    /// Stores the input's actions in order (at most <see cref="MaxActions"/>). A first action that is Default or Disabled means the input
    /// has no actions of its own to run, so any others are dropped with it.
    public void SetActions(int layer, MouseButton button, IReadOnlyList<string> specs)
    {
        var key = KeyOf(layer, button);
        ExtraActions.Remove(key);
        var first = specs.Count > 0 ? specs[0] : MouseActionCatalog.Default;
        Set(layer, button, first);
        if (MouseAction.Parse(first).Id is MouseActionCatalog.Default or MouseActionCatalog.Disabled) return;
        var extras = specs.Skip(1).Take(MaxActions - 1).ToList();
        if (extras.Count > 0) ExtraActions[key] = extras;
    }

    /// True when <paramref name="button"/> is remapped on some layer of its family and that action does not say "also let the original
    /// through": the input's own Windows action (a trackpad gesture's) is then to be turned off. An input at Default is never touched.
    /// With several actions the first one decides: only it offers "also let the original through" (<see cref="MouseOptions.OneShot"/>),
    /// since letting the input through is decided once per press.
    public bool ReplacesWindows(MouseButton button) => Enumerable.Range(1, Layers).Any(layer => ReplacesWindowsOn(layer, button));

    /// The same as <see cref="ReplacesWindows"/> for one layer: the input's first action there replaces its Windows action.
    public bool ReplacesWindowsOn(int layer, MouseButton button) =>
        Get(layer, button) is { } spec && MouseAction.Parse(spec) is var action
        && action.Id != MouseActionCatalog.Default && !action.Options.Bool(MouseOptions.KeepOriginal);

    /// True when one of the input's actions on <paramref name="layer"/> sends a shortcut that opens a Windows panel itself (Win+S, Alt+Tab...).
    public bool OpensWindowsPanel(int layer, MouseButton button) =>
        ActionsOf(layer, button).Any(spec => MouseActionCatalog.KeystrokeOf(MouseAction.Parse(spec)) is { } keys && KeyChord.OpensWindowsPanel(keys));

    /// Stores the input's first action; Default takes every action of the input on that layer away.
    public void Set(int layer, MouseButton button, string spec)
    {
        var key = KeyOf(layer, button);
        if (spec != MouseActionCatalog.Default)
        {
            Bindings[key] = spec;
            return;
        }
        Bindings.Remove(key);
        ExtraActions.Remove(key);
    }
}
