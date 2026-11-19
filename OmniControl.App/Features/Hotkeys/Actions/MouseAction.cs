using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Hotkeys.Actions;

/// A stored action read apart: its id, its text parameter (a shortcut, a path) and the values of its options.
/// Stored as "id", "id:parameter", and with options "id:parameter|key=value;key=value" (values are percent-encoded).
public sealed class MouseAction
{
    private const char OptionsSeparator = '|', PairSeparator = ';', ValueSeparator = '=';

    public string Id { get; }
    public string Param { get; }
    public OptionValues Options { get; }

    private MouseAction(string id, string param, OptionValues options)
    {
        Id = id;
        Param = param;
        Options = options;
    }

    public static MouseAction Parse(string stored)
    {
        var bar = stored.IndexOf(OptionsSeparator);
        var (id, param) = MouseActionCatalog.Split(bar < 0 ? stored : stored[..bar]);
        return new MouseAction(id, param, bar < 0 ? OptionValues.None : new OptionValues(ParseValues(stored[(bar + 1)..])));
    }

    /// An action kept as its "id:parameter" text and its option values apart (as a gesture stores it).
    public static MouseAction From(string action, IReadOnlyDictionary<string, string> values)
    {
        var (id, param) = MouseActionCatalog.Split(action);
        return new MouseAction(id, param, new OptionValues(new Dictionary<string, string>(values)));
    }

    /// Splits stored text into the action part ("id:parameter") and the option values.
    public static (string Action, Dictionary<string, string> Values) Split(string stored)
    {
        var bar = stored.IndexOf(OptionsSeparator);
        return bar < 0 ? (stored, new Dictionary<string, string>()) : (stored[..bar], ParseValues(stored[(bar + 1)..]));
    }

    /// The stored text for an action with these option values; values that are the default are left out.
    public static string Compose(string action, IReadOnlyList<ActionOption>? options, IReadOnlyDictionary<string, string> values)
    {
        var text = string.Join(PairSeparator,
            OptionValues.NonDefault(options, values).Select(v => $"{v.Key}{ValueSeparator}{Uri.EscapeDataString(v.Value)}"));
        return text.Length == 0 ? action : $"{action}{OptionsSeparator}{text}";
    }

    private static Dictionary<string, string> ParseValues(string text)
    {
        var values = new Dictionary<string, string>();
        foreach (var pair in text.Split(PairSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var at = pair.IndexOf(ValueSeparator);
            if (at > 0) values[pair[..at]] = Uri.UnescapeDataString(pair[(at + 1)..]);
        }
        return values;
    }
}
