using System.Text.Json.Serialization;
using OmniControl.App.Services;

namespace OmniControl.App.Automation;

/// <summary>What a custom action does with its target.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustomKind { Command, ScriptFile, Program, Path, Url }

/// <summary>A user-made action: a name, what kind it is, its target and (for a script or program) its arguments. It registers once in the master list.</summary>
public sealed class CustomAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public CustomKind Kind { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;

    [JsonIgnore]
    public string ActionId => ActionIdPrefix + Id;

    public const string ActionIdPrefix = "automation.custom.";
}

/// <summary>The words the type drop-down shows for each <see cref="CustomKind"/>, and what its target is called.</summary>
public static class CustomKinds
{
    private static readonly (CustomKind Kind, string Name, string TargetHint, bool TakesArguments)[] All =
    {
        (CustomKind.Command, "Run PowerShell command", "Start-Process notepad", false),
        (CustomKind.ScriptFile, "Run script file", @"C:\Scripts\backup.ps1 (or .bat, .cmd)", true),
        (CustomKind.Program, "Open program", @"C:\Path\App.exe", true),
        (CustomKind.Path, "Open file or folder", @"C:\Users\Me\Documents", false),
        (CustomKind.Url, "Open URL", "https://example.com", false),
    };

    public static IReadOnlyList<string> Names { get; } = All.Select(k => k.Name).ToList();

    /// <summary>The kinds whose target is words (a command, an address), a file or folder, or a program.</summary>
    public static IReadOnlyList<CustomKind> Typed { get; } = new[] { CustomKind.Command, CustomKind.Url };
    public static IReadOnlyList<CustomKind> Files { get; } = new[] { CustomKind.ScriptFile, CustomKind.Path };
    public static IReadOnlyList<CustomKind> Programs { get; } = new[] { CustomKind.Program };

    /// <summary>The picker a kind's target can be browsed with (a program, a script or file), or null when the target is words (a command, an address).</summary>
    public static PathKind? PickerOf(CustomKind kind) =>
        Programs.Contains(kind) ? PathKind.Program : Files.Contains(kind) ? PathKind.File : null;

    public static IReadOnlyList<CustomKind> WithArguments { get; } = All.Where(k => k.TakesArguments).Select(k => k.Kind).ToList();

    public static string[] NamesOf(IEnumerable<CustomKind> kinds) => kinds.Select(NameOf).ToArray();

    public static string NameOf(CustomKind kind) => All.First(k => k.Kind == kind).Name;

    public static CustomKind KindOf(string name) => All.FirstOrDefault(k => k.Name == name).Kind;

    public static string HintOf(CustomKind kind) => All.First(k => k.Kind == kind).TargetHint;

    public static bool TakesArguments(CustomKind kind) => All.First(k => k.Kind == kind).TakesArguments;
}
