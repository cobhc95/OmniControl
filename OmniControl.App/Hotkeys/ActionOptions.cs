using OmniControl.App.Services;

namespace OmniControl.App.Hotkeys;

public enum OptionKind { Number, Choice, Toggle, Text, Heading }

/// <summary>
/// One setting an action offers (how many times, which direction, which search engine...). Values are stored as text next to the action;
/// a value equal to <see cref="Default"/> is not stored, so an untouched action stays short.
/// </summary>
public sealed record ActionOption(string Key, string Label, OptionKind Kind, string Default,
    int Min = 0, int Max = 0, int Step = 1, string Unit = "", IReadOnlyList<string>? Choices = null, string Hint = "",
    string? ShowKey = null, IReadOnlyList<string>? ShowValues = null, PathKind? Browse = null)
{
    /// <summary>This setting only matters (and only shows) while another setting has one of these values.</summary>
    public ActionOption ShownWhen(ActionOption other, params string[] values) => this with { ShowKey = other.Key, ShowValues = values };

    /// <summary>True when the setting should show, given the current value of every setting by key.</summary>
    public bool IsShown(Func<string, string?> valueOf) => ShowKey is null || ShowValues!.Contains(valueOf(ShowKey));

    public static ActionOption Number(string key, string label, int initial, int min, int max, int step = 1, string unit = "") =>
        new(key, label, OptionKind.Number, initial.ToString(), min, max, step, unit);

    public static ActionOption Choice(string key, string label, IReadOnlyList<string> choices) =>
        new(key, label, OptionKind.Choice, choices[0], Choices: choices);

    public static ActionOption Toggle(string key, string label, bool on = false) =>
        new(key, label, OptionKind.Toggle, on ? "1" : "0");

    public static ActionOption Text(string key, string label, string initial = "", string hint = "") =>
        new(key, label, OptionKind.Text, initial, Hint: hint);

    /// <summary>A text that is a file, program or folder path: its row has the shared Browse button (<see cref="Controls.PathField"/>).</summary>
    public static ActionOption Path(string key, string label, PathKind kind, string hint = "") =>
        new(key, label, OptionKind.Text, "", Hint: hint, Browse: kind);

    /// <summary>A group's heading among the settings (holds no value); <paramref name="note"/> is a muted line under it when there is a fact to tell.</summary>
    public static ActionOption Heading(string key, string label, string note = "") =>
        new(key, label, OptionKind.Heading, "", Hint: note);
}

/// <summary>The values chosen for an action's options; anything not chosen reads as the option's default.</summary>
public sealed class OptionValues
{
    public static readonly OptionValues None = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> _values;

    public OptionValues(IReadOnlyDictionary<string, string> values) => _values = values;

    /// <summary>Only the values that differ from their option's default, for the options the action actually has.</summary>
    public static Dictionary<string, string> NonDefault(IReadOnlyList<ActionOption>? options, IReadOnlyDictionary<string, string> values) =>
        (options ?? Array.Empty<ActionOption>())
        .Where(o => values.TryGetValue(o.Key, out var v) && v != o.Default)
        .ToDictionary(o => o.Key, o => values[o.Key]);

    public int Int(ActionOption option) =>
        int.TryParse(Get(option), out var n) ? Math.Clamp(n, option.Min, option.Max) : int.Parse(option.Default);

    public bool Bool(ActionOption option) => Get(option) == "1";

    public string Text(ActionOption option) => Get(option);

    private string Get(ActionOption option) => _values.TryGetValue(option.Key, out var value) ? value : option.Default;
}
