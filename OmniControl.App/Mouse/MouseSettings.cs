using System.Text.Json.Serialization;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Mouse;

/// <summary>Mouse remapping preferences, stored as JSON in %APPDATA%\OmniControl\mouse.json.</summary>
public sealed class MouseSettings
{
    public const int Layers = 10;

    /// <summary>
    /// "Remap mouse and trackpad". While on, Windows' own gesture is turned off for every trackpad gesture that has an action which does
    /// not say "also let Windows do its own gesture" (<see cref="ReplacesWindows"/>, <c>TrackpadService</c>); there is no separate switch
    /// for that (a "TakeOverWindowsGestures" value in an older file is ignored).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The action per layer and input, keyed "layer.Button" (for example "1.Middle", "2.ThreeFingerUp"). The layer number belongs to the
    /// input's family (<see cref="InputFamilies.FamilyOf"/>): "2.Middle" is on mouse layer 2, "2.ThreeFingerUp" on trackpad layer 2.
    /// A missing key leaves the input as it is. Holds the input's first action; any others are in <see cref="ExtraActions"/>.
    /// </summary>
    public Dictionary<string, string> Bindings { get; set; } = new();

    /// <summary>
    /// The second and later actions of an input, keyed like <see cref="Bindings"/> (which keeps the first, so a file saved before inputs
    /// had several actions loads unchanged and an older version still reads the first). Kept only while the input has a first action
    /// of its own (<see cref="SetActions"/>); each entry is the same stored text as a binding.
    /// </summary>
    public Dictionary<string, List<string>> ExtraActions { get; set; } = new();

    /// <summary>The most actions one input can have on one layer.</summary>
    public const int MaxActions = 20;

    /// <summary>Names the user gave to mouse layers (layer number to name); a layer without one is called "Layer N".</summary>
    public Dictionary<int, string> LayerNames { get; set; } = new();

    /// <summary>True: the mouse Layer drop-down lists only layer 1, named layers and layers with mappings.</summary>
    public bool OnlyUsedLayers { get; set; } = true;

    /// <summary>Names the user gave to trackpad layers. Null only in a file saved before the trackpad had its own layers (<see cref="Upgrade"/>).</summary>
    public Dictionary<int, string>? TrackpadLayerNames { get; set; }

    /// <summary>True once <see cref="Upgrade"/> has removed trackpad names copied from the mouse's; it then never looks again.</summary>
    public bool TrackpadNamesIndependent { get; set; }

    /// <summary>True: the trackpad Layer drop-down lists only layer 1, named layers and layers with mappings.</summary>
    public bool TrackpadOnlyUsedLayers { get; set; } = true;

    /// <summary>Only read from files saved while the program still had profiles; <see cref="Upgrade"/> keeps the default profile's mappings and drops the rest.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<LegacyProfile>? Profiles { get; set; }

    public sealed class LegacyProfile
    {
        public Dictionary<string, string> Bindings { get; set; } = new();
    }

    private const string FileName = "mouse.json";

    public static MouseSettings Load()
    {
        var settings = AppDataStore.Load<MouseSettings>(FileName) ?? new();
        settings.Upgrade();
        return settings;
    }

    /// <summary>
    /// Brings a file from an older version up to date. Profiles: the default profile's mappings are kept. Shared layers (before the trackpad
    /// had its own): every gesture mapping keeps its layer number, so layer N's gestures are now trackpad layer N's; the trackpad's names
    /// start empty (never copied from the mouse's); and a gesture's layer action now moves the trackpad's layer.
    /// </summary>
    internal void Upgrade()
    {
        if (Profiles is { Count: > 0 } && Bindings.Count == 0) Bindings = Profiles[0].Bindings;
        Profiles = null;
        ExtraActions ??= new(); // a hand-edited file may say null
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

    public void Save() => AppDataStore.Save(FileName, this);

    /// <summary>One input family's layers: names, the "only used" choice, counts and clearing.</summary>
    public LayerSet LayersOf(InputFamily family) => new(this, family);

    public static string KeyOf(int layer, MouseButton button) => $"{layer}.{button}";

    /// <summary>The layer and input a stored key names, or null for a key this version can't read.</summary>
    public static (int Layer, MouseButton Button)? ParseKey(string key)
    {
        var dot = key.IndexOf('.');
        return dot > 0 && int.TryParse(key.AsSpan(0, dot), out var layer) && Enum.TryParse<MouseButton>(key.AsSpan(dot + 1), out var button)
            ? (layer, button)
            : null;
    }

    /// <summary>The input's first action on <paramref name="layer"/>, or null when it has none (Default).</summary>
    public string? Get(int layer, MouseButton button) => Bindings.GetValueOrDefault(KeyOf(layer, button));

    /// <summary>Every action of the input on <paramref name="layer"/>, in the order they run; empty when it has none (Default).</summary>
    public IReadOnlyList<string> ActionsOf(int layer, MouseButton button)
    {
        var key = KeyOf(layer, button);
        if (!Bindings.TryGetValue(key, out var first)) return Array.Empty<string>();
        return ExtraActions.TryGetValue(key, out var extras) ? extras.Prepend(first).ToList() : new[] { first };
    }

    /// <summary>
    /// Stores the input's actions in order (at most <see cref="MaxActions"/>). A first action that is Default or Disabled means the input
    /// has no actions of its own to run, so any others are dropped with it.
    /// </summary>
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

    /// <summary>
    /// True when <paramref name="button"/> is remapped on some layer of its family and that action does not say "also let the original
    /// through": the input's own Windows action (a trackpad gesture's) is then to be turned off. An input at Default is never touched.
    /// With several actions the first one decides: only it offers "also let the original through" (<see cref="MouseOptions.OneShot"/>),
    /// since letting the input through is decided once per press.
    /// </summary>
    public bool ReplacesWindows(MouseButton button) => Enumerable.Range(1, Layers).Any(layer => ReplacesWindowsOn(layer, button));

    /// <summary>The same as <see cref="ReplacesWindows"/> for one layer: the input's first action there replaces its Windows action.</summary>
    public bool ReplacesWindowsOn(int layer, MouseButton button) =>
        Get(layer, button) is { } spec && MouseAction.Parse(spec) is var action
        && action.Id != MouseActionCatalog.Default && !action.Options.Bool(MouseOptions.KeepOriginal);

    /// <summary>True when one of the input's actions on <paramref name="layer"/> sends a shortcut that opens a Windows panel itself (Win+S, Alt+Tab...).</summary>
    public bool OpensWindowsPanel(int layer, MouseButton button) =>
        ActionsOf(layer, button).Any(spec => MouseActionCatalog.KeystrokeOf(MouseAction.Parse(spec)) is { } keys && KeyChord.OpensWindowsPanel(keys));

    /// <summary>Stores the input's first action; Default takes every action of the input on that layer away.</summary>
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
