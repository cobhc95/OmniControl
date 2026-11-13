using OmniControl.App.Services;

namespace OmniControl.App.Gestures;

/// <summary>Whether one gesture is on, and which step action it drives.</summary>
public sealed class GestureRule
{
    public bool On { get; set; }
    public string Action { get; set; } = string.Empty;

    /// <summary>The values chosen for the action's options (only those that differ from the default).</summary>
    public Dictionary<string, string> Options { get; set; } = new();
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

    /// <summary>The rule for each gesture, by trigger id. A gesture with no entry yet is off with its default action.</summary>
    public Dictionary<string, GestureRule> Rules { get; set; } = new();

    private const string FileName = "gestures.json";

    public static GestureSettings Load() => AppDataStore.Load<GestureSettings>(FileName) ?? new();

    public void Save() => AppDataStore.Save(FileName, this);

    /// <summary>The rule for a trigger, created (off, default action) the first time it is asked for.</summary>
    public GestureRule RuleFor(GestureTrigger trigger)
    {
        if (!Rules.TryGetValue(trigger.Id, out var rule))
            Rules[trigger.Id] = rule = new GestureRule { Action = trigger.DefaultAction };
        return rule;
    }
}
