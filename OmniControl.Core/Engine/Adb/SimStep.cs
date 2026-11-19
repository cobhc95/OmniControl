using System.Text.Json.Serialization;

namespace OmniControl.Core.Engine;

/// What one recorded step of a simulated device does on the Android screen.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SimStepKind { OpenApp, WaitFor, Tap, TypeText, Swipe, Back, Check }

/// One thing a step can be told: each is a text box in the step's editor.
public enum SimField { Package, Text, Description, ResourceId, Direction }

/// One recorded step. An element is found by its text, its content description or its resource id (<see cref="SimScreen.Find"/>);
/// the position (<see cref="X"/>, <see cref="Y"/>, -1 = none) is only used when the element has none of those.
public sealed class SimStep
{
    public SimStepKind Kind { get; set; }
    public string Package { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;

    /// A swipe's way: up, down, left or right.
    public string Direction { get; set; } = string.Empty;

    /// What a person reads for the step ("Tapo", "Orbit 11's power button"); display only, the matcher fields above stay precise. Empty = made from the matcher.
    public string Label { get; set; } = string.Empty;

    /// The text of the card or row the element sits in, when several elements look alike (every device card has the same power button): the match is the one inside that card.
    public string Anchor { get; set; } = string.Empty;

    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;

    public SimStep Clone() => (SimStep)MemberwiseClone();

    public bool HasIdentity => Text.Length > 0 || Description.Length > 0 || ResourceId.Length > 0;
    public bool HasPosition => X >= 0 && Y >= 0;
}

/// A step as a sentence: "Wait for" + <c>"Power"</c> + "· tap it".
public sealed record SimStepWords(string Lead, string Target, string Tail);

/// The master list of step kinds and everything that depends on the kind (its words, its fields, what makes it complete).
public static class SimSteps
{
    public static readonly string[] Directions = { "up", "down", "left", "right" };

    /// The system bars; never the app a person works in.
    public const string SystemUiPackage = "com.android.systemui";

    /// A home screen (launcher): opening an app from it is recorded as Open app, and it is never the app to open.
    public static bool IsLauncher(string package) =>
        package.Length == 0 || package == SystemUiPackage || package.Contains("launcher", StringComparison.OrdinalIgnoreCase) || package.EndsWith(".home", StringComparison.OrdinalIgnoreCase);

    private sealed record Info(SimStepKind Kind, string Name, string Lead, SimField[] Fields);

    private static readonly Info[] Kinds =
    {
        new(SimStepKind.OpenApp, "Open app", "Open app", new[] { SimField.Package }),
        new(SimStepKind.WaitFor, "Wait for", "Wait for", new[] { SimField.Text, SimField.Description, SimField.ResourceId }),
        new(SimStepKind.Tap, "Tap", "Wait for", new[] { SimField.Text, SimField.Description, SimField.ResourceId }),
        new(SimStepKind.TypeText, "Type text", "Type", new[] { SimField.Text }),
        new(SimStepKind.Swipe, "Swipe", "Swipe", new[] { SimField.Direction }),
        new(SimStepKind.Back, "Back", "Press", Array.Empty<SimField>()),
        new(SimStepKind.Check, "Check", "Check screen shows", new[] { SimField.Text, SimField.Description, SimField.ResourceId }),
    };

    /// Every step kind, in the order the "+ add step" menu lists them.
    public static IReadOnlyList<SimStepKind> All { get; } = Kinds.Select(k => k.Kind).ToList();

    public static string NameOf(SimStepKind kind) => Of(kind).Name;

    public static IReadOnlyList<SimField> FieldsOf(SimStepKind kind) => Of(kind).Fields;

    public static string LabelOf(SimField field) => field switch
    {
        SimField.Package => "App package (for example com.tplink.iot)",
        SimField.Text => "Text on the screen",
        SimField.Description => "Description (content-desc)",
        SimField.ResourceId => "Resource id",
        _ => "Direction (up, down, left, right)",
    };

    public static string Get(SimStep step, SimField field) => field switch
    {
        SimField.Package => step.Package,
        SimField.Text => step.Text,
        SimField.Description => step.Description,
        SimField.ResourceId => step.ResourceId,
        _ => step.Direction,
    };

    public static void Set(SimStep step, SimField field, string value)
    {
        value = value.Trim();
        if (field != SimField.Direction) step.Label = string.Empty; // an edited matcher no longer matches its old name
        switch (field)
        {
            case SimField.Package: step.Package = value; break;
            case SimField.Text: step.Text = value; break;
            case SimField.Description: step.Description = value; break;
            case SimField.ResourceId: step.ResourceId = value; break;
            default: step.Direction = value.ToLowerInvariant(); break;
        }
    }

    public static SimStep Blank(SimStepKind kind) => new() { Kind = kind, Direction = kind == SimStepKind.Swipe ? "up" : string.Empty };

    /// Why the step cannot run yet, as a short phrase; null when it is complete.
    public static string? Missing(SimStep step) => step.Kind switch
    {
        SimStepKind.OpenApp => IsPackage(step.Package) ? null : "the app's package name is missing",
        SimStepKind.WaitFor or SimStepKind.Tap => step.HasIdentity || step.HasPosition ? null : "no element is picked",
        SimStepKind.Check => step.Text.Length > 0 || step.Description.Length > 0 ? null : "no text is picked",
        SimStepKind.TypeText => step.Text.Length > 0 ? null : "no text is typed",
        SimStepKind.Swipe => Directions.Contains(step.Direction) ? null : "the direction is not up, down, left or right",
        _ => null,
    };

    /// One plain sentence saying what step number <paramref name="number"/> still needs and what to do about it; null when it is complete.
    public static string? Fix(SimStep step, int number)
    {
        if (Missing(step) is null) return null;
        var what = step.Kind switch
        {
            SimStepKind.OpenApp => "no app. Type its package name or delete it",
            SimStepKind.WaitFor => "nothing to wait for. Re-pick it or delete it",
            SimStepKind.Tap => "nothing to tap. Re-pick it or delete it",
            SimStepKind.Check => "nothing to check. Re-pick it or delete it",
            SimStepKind.TypeText => "no text to type. Type it or delete it",
            _ => "no direction. Use up, down, left or right",
        };
        return $"Step {number} has {what}.";
    }

    /// An Android package name; also the only thing that is ever put into an adb shell command from a step.
    public static bool IsPackage(string text) => text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_');

    /// The words of a step: what it does, what it is about (a block) and what follows.
    public static SimStepWords Words(SimStep step)
    {
        var info = Of(step.Kind);
        var target = step.Kind switch
        {
            SimStepKind.OpenApp => step.Label.Length > 0 ? step.Label : step.Package,
            SimStepKind.Swipe => step.Direction,
            SimStepKind.Back => "Back",
            _ => Named(step),
        };
        var tail = step.Kind == SimStepKind.Tap ? "· tap it" : string.Empty;
        return new SimStepWords(info.Lead, target, tail);
    }

    /// The exact matcher of a step, for its tooltip (package, text, id and position); empty when there is nothing beyond its label.
    public static string Detail(SimStep step)
    {
        var parts = new List<string>();
        if (step.Package.Length > 0) parts.Add(step.Package);
        if (step.Anchor.Length > 0) parts.Add($"in the card with \"{step.Anchor}\"");
        if (step.Text.Length > 0) parts.Add($"text \"{step.Text}\"");
        if (step.Description.Length > 0) parts.Add($"description \"{step.Description}\"");
        if (step.ResourceId.Length > 0) parts.Add(step.ResourceId);
        if (step.HasPosition) parts.Add($"position {step.X}, {step.Y}");
        return string.Join(" · ", parts);
    }

    /// "device_switch" or "deviceSwitch" as "device switch"; first letter capital when <paramref name="capital"/>.
    public static string Humanise(string id, bool capital = true)
    {
        var name = System.Text.RegularExpressions.Regex.Replace(id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : id, "([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ').Trim().ToLowerInvariant();
        return capital && name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
    }

    private static string Named(SimStep step)
    {
        if (step.Label.Length > 0) return step.Label;
        var name = step.Text.Length > 0 ? step.Text : step.Description.Length > 0 ? step.Description : step.ResourceId;
        if (name.Length > 0) return $"\"{name}\"";
        return step.HasPosition ? "a spot on the screen" : string.Empty;
    }

    private static Info Of(SimStepKind kind) => Kinds.First(k => k.Kind == kind);
}
