namespace OmniControl.App.Mouse;

/// <summary>
/// One entry of a picker list (an action, a rule's trigger or condition...). <see cref="Spec"/> is what is stored; an entry with a
/// <see cref="ParamHint"/> takes text after the colon. <see cref="Name"/> is unique across its list, so it is also what a card shows.
/// <see cref="Keywords"/> are extra words the search matches (the shortcut, or what it is called elsewhere). <see cref="Options"/> are
/// the settings it offers (behind the cogwheel); <see cref="ParamLabel"/> names the text a ParamHint entry takes, in those settings.
/// <see cref="Unavailable"/> is why the entry can't be picked here (it is shown greyed with that reason); null = it can.
/// <see cref="AskFirst"/>: the entry is not complete without its settings (a command to run), so a picker that asks for them opens its settings page when it is picked.
/// </summary>
public sealed record PickChoice(string Spec, string Category, string Name, string? ParamHint = null, string Keywords = "",
    IReadOnlyList<ActionOption>? Options = null, string ParamLabel = "", string? Unavailable = null, bool AskFirst = false)
{
    public bool HasOptions => Options is { Count: > 0 };

    /// <summary>True when the entry needs text or settings before it can be used.</summary>
    public bool NeedsSettings => ParamHint is not null || AskFirst;

    /// <summary>True when a stored entry ("id" or "id:parameter", without options) is this choice.</summary>
    public bool Matches(string stored) => ParamHint is null ? Spec == stored : stored.StartsWith(Spec, StringComparison.Ordinal);

    /// <summary>The text after the colon of a stored entry that <see cref="Matches"/> this choice; empty for a choice that takes none.</summary>
    public string ParameterOf(string stored) => ParamHint is null ? string.Empty : stored[Spec.Length..];

    /// <summary>The stored entry ("id" or "id:parameter", without options) for this choice with that text.</summary>
    public string WithParameter(string parameter) => ParamHint is null ? Spec : Spec + parameter;

    /// <summary>Gathers choices into categories, in order of first appearance.</summary>
    public static List<PickGroup> Group(IEnumerable<PickChoice> choices) =>
        choices.GroupBy(c => c.Category).Select(g => new PickGroup(g.Key, g.ToList())).ToList();
}

/// <summary>One category of a picker list.</summary>
public sealed record PickGroup(string Name, IReadOnlyList<PickChoice> Choices);
