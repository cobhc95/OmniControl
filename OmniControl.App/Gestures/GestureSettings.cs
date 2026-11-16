using OmniControl.App.AppLists;
using OmniControl.App.Services;

namespace OmniControl.App.Gestures;

/// <summary>Whether one gesture is on, and which step action it drives.</summary>
public sealed class GestureRule
{
    public bool On { get; set; }
    public string Action { get; set; } = string.Empty;

    /// <summary>The values chosen for the action's options (only those that differ from the default).</summary>
    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>The gesture's own apps scope (only a "Scroll over a screen area" card has one); null: it follows its group's.</summary>
    public AppScopeSetting? Scope { get; set; }
}

/// <summary>Gesture preferences, stored as JSON in %APPDATA%\OmniControl\gestures.json. Every gesture starts off.</summary>
public sealed class GestureSettings
{
    public const int MinEdgeStrip = 2, MaxEdgeStrip = 40, DefaultEdgeStrip = 6;

    /// <summary>The edge strip's name and unit, as the slider and the on-screen margins show them.</summary>
    public const string EdgeStripLabel = "Edge strip width", EdgeStripUnit = " px";

    public bool Enabled { get; set; } = true;

    /// <summary>How many pixels deep the strip along a screen edge is.</summary>
    public int EdgeStrip { get; set; } = DefaultEdgeStrip;

    /// <summary>The edge strip as gestures use it: a hand-edited file cannot put it outside the slider's range.</summary>
    public int ClampedEdgeStrip() => Math.Clamp(EdgeStrip, MinEdgeStrip, MaxEdgeStrip);

    /// <summary>The apps the "Scroll over a screen area" gestures work in (null: every app).</summary>
    public AppScopeSetting? AreasScope { get; set; }

    /// <summary>The rule for each gesture, by trigger id. A gesture with no entry yet is off with its default action.</summary>
    public Dictionary<string, GestureRule> Rules { get; set; } = new();

    private static readonly DataStore Store = DataStores.Gestures;

    public static GestureSettings Load() => AppDataStore.Load<GestureSettings>(Store) ?? new();

    public void Save() => AppDataStore.Save(Store, this);

    /// <summary>
    /// Takes every value of <paramref name="other"/>. The rule objects stay (the page's cards hold them): each is set to the one in
    /// <paramref name="other"/>, or back to off with its default action when <paramref name="other"/> has none.
    /// </summary>
    public void CopyFrom(GestureSettings other)
    {
        Enabled = other.Enabled;
        EdgeStrip = other.EdgeStrip;
        AreasScope = other.AreasScope;
        foreach (var trigger in GestureCatalog.Triggers)
        {
            var rule = RuleFor(trigger);
            var source = other.Rules.GetValueOrDefault(trigger.Id);
            rule.On = source?.On ?? false;
            rule.Action = source?.Action ?? trigger.DefaultAction;
            rule.Options = source is null ? new() : new Dictionary<string, string>(source.Options);
            rule.Scope = source?.Scope;
        }
    }

    /// <summary>The apps scope in force for a "Scroll over a screen area" gesture: its own, else the group's; null when it works in every app.</summary>
    public AppScopeSetting? AreaScopeOf(GestureTrigger trigger)
    {
        var scope = RuleFor(trigger).Scope ?? AreasScope;
        return scope is { IsLimited: true } ? scope : null;
    }

    /// <summary>The rule for a trigger, created (off, default action) the first time it is asked for.</summary>
    public GestureRule RuleFor(GestureTrigger trigger)
    {
        if (!Rules.TryGetValue(trigger.Id, out var rule))
            Rules[trigger.Id] = rule = new GestureRule { Action = trigger.DefaultAction };
        return rule;
    }
}
