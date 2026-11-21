using OmniControl.App.Features.AppLists;

namespace OmniControl.App.Features.Input.Gestures;

/// Whether one gesture is on, and which step action it drives.
public sealed class GestureRule
{
    public bool On { get; set; }
    public string Action { get; set; } = string.Empty;

    /// The values chosen for the action's options (only those that differ from the default).
    public Dictionary<string, string> Options { get; set; } = new();

    /// The gesture's own apps scope; null: it follows its group's.
    public AppScopeSetting? Scope { get; set; }
}

/// Gesture preferences, stored as JSON in %APPDATA%\OmniControl\gestures.json. A gesture starts as its trigger's DefaultOn says.
public sealed class GestureSettings
{
    public const int MinEdgeStrip = 2, MaxEdgeStrip = 40, DefaultEdgeStrip = 6;

    /// The edge strip's name and unit, as the slider and the on-screen margins show them.
    public const string EdgeStripLabel = "Edge strip width", EdgeStripUnit = " px";

    public const bool DefaultEnabled = true;

    public bool Enabled { get; set; } = DefaultEnabled;

    /// How many pixels deep the strip along a screen edge is.
    public int EdgeStrip { get; set; } = DefaultEdgeStrip;

    /// The edge strip as gestures use it: a hand-edited file cannot put it outside the slider's range.
    public int ClampedEdgeStrip() => Math.Clamp(EdgeStrip, MinEdgeStrip, MaxEdgeStrip);

    /// The apps each group's gestures work in (null: every app): scroll over a screen area, middle click over one, scroll while holding a key.
    public AppScopeSetting? AreasScope { get; set; }
    public AppScopeSetting? MiddleScope { get; set; }
    public AppScopeSetting? KeysScope { get; set; }

    public AppScopeSetting? GroupScopeOf(string group) => group switch
    {
        GestureCatalog.AreaGroup => AreasScope,
        GestureCatalog.MiddleGroup => MiddleScope,
        _ => KeysScope,
    };

    public void SetGroupScope(string group, AppScopeSetting? scope)
    {
        switch (group)
        {
            case GestureCatalog.AreaGroup: AreasScope = scope; break;
            case GestureCatalog.MiddleGroup: MiddleScope = scope; break;
            default: KeysScope = scope; break;
        }
    }

    /// The rule for each gesture, by trigger id. A gesture with no entry yet is off with its default action.
    public Dictionary<string, GestureRule> Rules { get; set; } = new();

    private static readonly DataStore Store = DataStores.Gestures;

    public static GestureSettings Load() => AppDataStore.Load<GestureSettings>(Store) ?? new();

    public void Save() => AppDataStore.SaveSoon(Store, this);

    /// Takes every value of <paramref name="other"/>. The rule objects stay (the page's cards hold them): each is set to the one in
    /// <paramref name="other"/>, or back to off with its default action when <paramref name="other"/> has none.
    public void CopyFrom(GestureSettings other)
    {
        Enabled = other.Enabled;
        EdgeStrip = other.EdgeStrip;
        AreasScope = other.AreasScope;
        MiddleScope = other.MiddleScope;
        KeysScope = other.KeysScope;
        foreach (var trigger in GestureCatalog.Triggers)
        {
            var rule = RuleFor(trigger);
            var source = other.Rules.GetValueOrDefault(trigger.Id);
            rule.On = source?.On ?? trigger.DefaultOn;
            rule.Action = source?.Action ?? trigger.DefaultAction;
            rule.Options = source is null ? new() : new Dictionary<string, string>(source.Options);
            rule.Scope = source?.Scope;
        }
    }

    /// The apps scope in force for a gesture: its own, else its group's; null when it works in every app.
    public AppScopeSetting? ScopeOf(GestureTrigger trigger)
    {
        var scope = RuleFor(trigger).Scope ?? GroupScopeOf(trigger.Group);
        return scope is { IsLimited: true } ? scope : null;
    }

    /// The rule for a trigger, created (off, default action) the first time it is asked for.
    public GestureRule RuleFor(GestureTrigger trigger)
    {
        if (!Rules.TryGetValue(trigger.Id, out var rule))
            Rules[trigger.Id] = rule = new GestureRule { On = trigger.DefaultOn, Action = trigger.DefaultAction };
        return rule;
    }
}
