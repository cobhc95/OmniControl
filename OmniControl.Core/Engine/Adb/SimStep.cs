using System.Text.Json.Serialization;

namespace OmniControl.Core.Engine;

/// <summary>What one recorded step of a simulated device does on the Android screen.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SimStepKind { OpenApp, WaitFor, Tap, TypeText, Swipe, Back, Check }

/// <summary>One thing a step can be told: each is a text box in the step's editor.</summary>
public enum SimField { Package, Text, Description, ResourceId, Direction }

/// <summary>
/// One recorded step. An element is found by its text, its content description or its resource id (<see cref="SimScreen.Find"/>);
/// the position (<see cref="X"/>, <see cref="Y"/>, -1 = none) is only used when the element has none of those.
/// </summary>
public sealed class SimStep
{
    public SimStepKind Kind { get; set; }
    public string Package { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>A swipe's way: up, down, left or right.</summary>
    public string Direction { get; set; } = string.Empty;

    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;

    public bool HasIdentity => Text.Length > 0 || Description.Length > 0 || ResourceId.Length > 0;
    public bool HasPosition => X >= 0 && Y >= 0;
}

/// <summary>A step as a sentence: "Wait for" + <c>"Power"</c> + "· tap it".</summary>
public sealed record SimStepWords(string Lead, string Target, string Tail);

/// <summary>The master list of step kinds and everything that depends on the kind (its words, its fields, what makes it complete).</summary>
public static class SimSteps
{
    public static readonly string[] Directions = { "up", "down", "left", "right" };

    private sealed record Info(SimStepKind Kind, string Name, string Lead, SimField[] Fields);

    private static readonly Info[] Kinds =
    {
        new(SimStepKind.OpenApp, "Open app", "Open app", new[] { SimField.Package }),
        new(SimStepKind.WaitFor, "Wait for", "Wait for", new[] { SimField.Text, SimField.Description, SimField.ResourceId }),
        new(SimStepKind.Tap, "Tap", "Wait for", new[] { SimField.Text, SimField.Description, SimField.ResourceId }),
        new(SimStepKind.TypeText, "Type text", "Type", new[] { SimField.Text }),
        new(SimStepKind.Swipe, "Swipe", "Swipe", new[] { SimField.Direction }),
        new(SimStepKind.Back, "Back", "Press", Array.Empty<SimField>()),
        new(SimStepKind.Check, "Check", "Check screen shows", new[] { SimField.Text, SimField.Description }),
    };

    /// <summary>Every step kind, in the order the "+ add step" menu lists them.</summary>
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

    /// <summary>Why the step cannot run yet, as a short phrase; null when it is complete.</summary>
    public static string? Missing(SimStep step) => step.Kind switch
    {
        SimStepKind.OpenApp => IsPackage(step.Package) ? null : "the app's package name is missing",
        SimStepKind.WaitFor or SimStepKind.Tap => step.HasIdentity || step.HasPosition ? null : "no element is picked",
        SimStepKind.Check => step.Text.Length > 0 || step.Description.Length > 0 ? null : "no text is picked",
        SimStepKind.TypeText => step.Text.Length > 0 ? null : "no text is typed",
        SimStepKind.Swipe => Directions.Contains(step.Direction) ? null : "the direction is not up, down, left or right",
        _ => null,
    };

    /// <summary>An Android package name; also the only thing that is ever put into an adb shell command from a step.</summary>
    public static bool IsPackage(string text) => text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_');

    /// <summary>The words of a step: what it does, what it is about (a block) and what follows.</summary>
    public static SimStepWords Words(SimStep step)
    {
        var info = Of(step.Kind);
        var target = step.Kind switch
        {
            SimStepKind.OpenApp => step.Package,
            SimStepKind.Swipe => step.Direction,
            SimStepKind.Back => "Back",
            _ => Named(step),
        };
        var tail = step.Kind == SimStepKind.Tap ? "· tap it" : string.Empty;
        return new SimStepWords(info.Lead, target, tail);
    }

    private static string Named(SimStep step)
    {
        var name = step.Text.Length > 0 ? step.Text : step.Description.Length > 0 ? step.Description : step.ResourceId;
        if (name.Length > 0) return $"\"{name}\"";
        return step.HasPosition ? $"the spot at {step.X}, {step.Y}" : string.Empty;
    }

    private static Info Of(SimStepKind kind) => Kinds.First(k => k.Kind == kind);
}
