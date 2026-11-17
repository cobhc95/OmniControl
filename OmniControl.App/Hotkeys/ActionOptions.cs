using OmniControl.App.Services;

namespace OmniControl.App.Hotkeys;

public enum OptionKind { Number, Choice, Toggle, Text, Heading, Point, Panel }

/// <summary>
/// One setting an action offers (how many times, which direction, which search engine...). Values are stored as text next to the action;
/// a value equal to <see cref="Default"/> is not stored, so an untouched action stays short.
/// </summary>
public sealed record ActionOption(string Key, string Label, OptionKind Kind, string Default,
    int Min = 0, int Max = 0, int Step = 1, string Unit = "", IReadOnlyList<string>? Choices = null, string Hint = "",
    string? ShowKey = null, IReadOnlyList<string>? ShowValues = null, PathKind? Browse = null,
    bool Shared = false, string? NeedsKey = null, string NeedsReason = "", Func<ISettingsPanel>? MakePanel = null, Func<string?>? TagNow = null)
{
    /// <summary>The value lives once, outside the action (<see cref="ISharedOptions"/>), and is the same for every action that offers it.</summary>
    public ActionOption SharedSetting() => this with { Shared = true };

    /// <summary>The row stays but is dimmed, with <paramref name="reason"/> as its tooltip, until the toggle <paramref name="other"/> is on.</summary>
    public ActionOption NeedsOn(ActionOption other, string reason) => this with { NeedsKey = other.Key, NeedsReason = reason };

    /// <summary>False while the setting this one needs is off.</summary>
    public bool IsAvailable(Func<string, string?> valueOf) => NeedsKey is null || valueOf(NeedsKey) == "1";

    /// <summary>This setting only matters (and only shows) while another setting has one of these values.</summary>
    public ActionOption ShownWhen(ActionOption other, params string[] values) => this with { ShowKey = other.Key, ShowValues = values };

    /// <summary>True when the setting should show, given the current value of every setting by key.</summary>
    public bool IsShown(Func<string, string?> valueOf) => ShowKey is null || ShowValues!.Contains(valueOf(ShowKey));

    public static ActionOption Number(string key, string label, int initial, int min, int max, int step = 1, string unit = "") =>
        new(key, label, OptionKind.Number, initial.ToString(), min, max, step, unit);

    public static ActionOption Choice(string key, string label, IReadOnlyList<string> choices) =>
        new(key, label, OptionKind.Choice, choices[0], Choices: choices);

    /// <summary>A switch; <paramref name="note"/> is a muted line under its label when there is a fact to tell.</summary>
    public static ActionOption Toggle(string key, string label, bool on = false, string note = "") =>
        new(key, label, OptionKind.Toggle, on ? "1" : "0", Hint: note);

    /// <summary>A caution tag on the row while <paramref name="now"/> returns its words (null = no tag); asked again each time the row's value changes.</summary>
    public ActionOption TaggedWhen(Func<string?> now) => this with { TagNow = now };

    public static ActionOption Text(string key, string label, string initial = "", string hint = "") =>
        new(key, label, OptionKind.Text, initial, Hint: hint);

    /// <summary>A text that is a file, program or folder path: its row has the shared Browse button (<see cref="Controls.PathField"/>).</summary>
    public static ActionOption Path(string key, string label, PathKind kind, string hint = "") =>
        new(key, label, OptionKind.Text, "", Hint: hint, Browse: kind);

    /// <summary>A point on the screen ("x,y" in pixels, empty until picked): its row shows it with the shared "Pick…" (<see cref="Controls.PointField"/>).</summary>
    public static ActionOption Point(string key, string label) =>
        new(key, label, OptionKind.Point, "");

    /// <summary>A whole editor of its own as one row (the power plan editor); <paramref name="make"/> builds its view-model each time the settings pane shows it. It holds no stored value.</summary>
    public static ActionOption Panel(string key, string label, Func<ISettingsPanel> make) =>
        new(key, label, OptionKind.Panel, "", MakePanel: make);

    /// <summary>A group's heading among the settings (holds no value); <paramref name="note"/> is a muted line under it when there is a fact to tell.</summary>
    public static ActionOption Heading(string key, string label, string note = "") =>
        new(key, label, OptionKind.Heading, "", Hint: note);
}

/// <summary>The view-model of an <see cref="OptionKind.Panel"/> row: the settings pane tells it when a typed value is cancelled (Esc).</summary>
public interface ISettingsPanel
{
    void CancelTyping();
}

/// <summary>Where the value of a <see cref="ActionOption.Shared"/> setting is kept (its owner's settings), read and written as the row changes.</summary>
public interface ISharedOptions
{
    string Read(ActionOption option);
    void Write(ActionOption option, string value);
}

/// <summary>The values chosen for an action's options; anything not chosen reads as the option's default.</summary>
public sealed class OptionValues
{
    public static readonly OptionValues None = new(new Dictionary<string, string>());

    /// <summary>
    /// Stored with an input's values (a button, gesture or rule step) when that input keeps its own copy of an OmniControl action's settings;
    /// without it the input uses the action's own defaults (<see cref="HotkeyAction.Defaults"/>), which its hotkeys and rules use too.
    /// </summary>
    public const string OwnKey = "own";

    private readonly IReadOnlyDictionary<string, string> _values;

    public OptionValues(IReadOnlyDictionary<string, string> values) => _values = values;

    /// <summary>True when the value of this setting was stored (it differs from its default).</summary>
    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>Only the values that differ from their option's default, for the options the action actually has (and the own-copy mark, when set).</summary>
    public static Dictionary<string, string> NonDefault(IReadOnlyList<ActionOption>? options, IReadOnlyDictionary<string, string> values)
    {
        var kept = (options ?? Array.Empty<ActionOption>())
            .Where(o => values.TryGetValue(o.Key, out var v) && v != o.Default)
            .ToDictionary(o => o.Key, o => values[o.Key]);
        if (values.TryGetValue(OwnKey, out var own) && own == "1") kept[OwnKey] = own;
        return kept;
    }

    public int Int(ActionOption option) =>
        int.TryParse(Get(option), out var n) ? Math.Clamp(n, option.Min, option.Max) : int.Parse(option.Default);

    public bool Bool(ActionOption option) => Get(option) == "1";

    public string Text(ActionOption option) => Get(option);

    /// <summary>The picked point of a <see cref="ActionOption.Point"/> setting; null until one is picked.</summary>
    public (int X, int Y)? Point(ActionOption option) => ScreenPointText.TryRead(Get(option));

    private string Get(ActionOption option) => _values.TryGetValue(option.Key, out var value) ? value : option.Default;
}
