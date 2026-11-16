using Avalonia;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;

namespace OmniControl.App.Mouse;

/// <summary>
/// One entry of a picker list (an action, a rule's trigger or condition...). <see cref="Spec"/> is what is stored; an entry with a
/// <see cref="ParamHint"/> takes text after the colon. <see cref="Name"/> is unique across its list, so it is also what a card shows.
/// <see cref="Keywords"/> are extra words the search matches (the shortcut, or what it is called elsewhere). <see cref="Options"/> are
/// the settings it offers (behind the cogwheel); <see cref="ParamLabel"/> names the text a ParamHint entry takes, in those settings.
/// <see cref="Unavailable"/> is why the entry can't be picked here (it is shown greyed with that reason); null = it can.
/// <see cref="ParamBrowse"/>: the text is a path, so its box has the Browse button for that kind of path.
/// <see cref="AskFirst"/>: the entry is not complete without its settings (a command to run), so a picker that asks for them opens its settings page when it is picked.
/// <see cref="Remove"/>: the user may take the entry out of the list (it shows a remove cross). <see cref="Command"/>: the line is a button of its list (add a
/// program), not an action: picking it runs the command, which returns the entries to add. <see cref="Opened"/>: called each time a picker listing the entry opens.
/// <see cref="Category"/> is the entry's section in the Choose-the-action window (its pane 1); <see cref="Heading"/> is the small group heading it sits under in the
/// section's list, and <see cref="Place"/> the section's icon and order (read from the live page list; null in a list that has none). <see cref="RemoveText"/> names the
/// <see cref="Icon"/> is the entry's own glyph (a resource key of Styles/Brushes.axaml; null: its section's icon). <see cref="RemoveText"/> names the
/// remove button, <see cref="Renaming"/> lets the user rename it, <see cref="IsListed"/> false hides a list button for now, <see cref="Editor"/> holds the data the settings pane edits live (a custom action) and <see cref="Create"/> makes a new entry from the form's values (the entry is that form, not an action).
/// </summary>
public sealed record PickChoice(string Spec, string Category, string Name, string? ParamHint = null, string Keywords = "",
    IReadOnlyList<ActionOption>? Options = null, string ParamLabel = "", string? Unavailable = null, bool AskFirst = false, PathKind? ParamBrowse = null,
    Action? Remove = null, Func<Visual, Task<IReadOnlyList<PickChoice>>>? Command = null, Action? Opened = null,
    string? Heading = null, ActionSection? Place = null, string RemoveText = "Remove", Renaming? Renaming = null, Func<bool>? IsListed = null,
    ActionEditor? Editor = null, Func<IReadOnlyDictionary<string, string>, PickChoice>? Create = null, string? Icon = null)
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

    /// <summary>
    /// Gathers choices into sections: the ones with a place (a page, or Power, Launch...) first in that order, then the rest in order of first
    /// appearance. A section takes the place and icon of its first choice that has one.
    /// </summary>
    public static List<PickGroup> Group(IEnumerable<PickChoice> choices) =>
        choices.GroupBy(c => c.Category)
            .Select(g => new PickGroup(g.Key, g.ToList(), g.Select(c => c.Place).FirstOrDefault(p => p is not null)))
            .OrderBy(g => g.Place?.Rank ?? int.MaxValue) // the sort is stable, so sections without a place keep their order
            .ToList();
}

/// <summary>One section of a picker list; <see cref="Place"/> is its icon and position when the list has them.</summary>
public sealed record PickGroup(string Name, IReadOnlyList<PickChoice> Choices, ActionSection? Place = null);
