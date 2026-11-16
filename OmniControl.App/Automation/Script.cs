using System.Text.Json.Serialization;

namespace OmniControl.App.Automation;

/// <summary>The language a saved script is written in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScriptLanguage { PowerShell, Batch, Python }

/// <summary>A script kept inside OmniControl's data (automation.json): a name, its language and its text. It registers one action, "Run script: name".</summary>
public sealed class Script
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public ScriptLanguage Language { get; set; }
    public string Body { get; set; } = string.Empty;

    [JsonIgnore]
    public string ActionId => ActionIdPrefix + Id;

    public const string ActionIdPrefix = "automation.script.";
}

/// <summary>The words, file ending and file types of each <see cref="ScriptLanguage"/>, written once.</summary>
public static class ScriptLanguages
{
    private static readonly (ScriptLanguage Language, string Name, string Extension, string[] Patterns)[] All =
    {
        (ScriptLanguage.PowerShell, "PowerShell", ".ps1", new[] { "*.ps1" }),
        (ScriptLanguage.Batch, "CMD batch", ".cmd", new[] { "*.bat", "*.cmd" }),
        (ScriptLanguage.Python, "Python", ".py", new[] { "*.py", "*.pyw" }),
    };

    /// <summary>The languages that can be chosen now: Python only when an installed Python was found.</summary>
    public static IReadOnlyList<ScriptLanguage> Offered(bool hasPython) =>
        All.Select(l => l.Language).Where(l => hasPython || l != ScriptLanguage.Python).ToList();

    public static string NameOf(ScriptLanguage language) => All.First(l => l.Language == language).Name;

    public static ScriptLanguage OfName(string name) => All.FirstOrDefault(l => l.Name == name).Language;

    public static string ExtensionOf(ScriptLanguage language) => All.First(l => l.Language == language).Extension;

    /// <summary>Every script file type, for the Import and Export pickers.</summary>
    public static IReadOnlyList<string> Patterns { get; } = All.SelectMany(l => l.Patterns).ToList();

    /// <summary>The language a file is written in, by its ending, or null when it is not a script.</summary>
    public static ScriptLanguage? OfFile(string path)
    {
        var ending = "*" + Path.GetExtension(path).ToLowerInvariant();
        return All.Where(l => l.Patterns.Contains(ending)).Select(l => (ScriptLanguage?)l.Language).FirstOrDefault();
    }
}
