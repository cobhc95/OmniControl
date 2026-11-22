using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Hotkeys.Actions;

/// One entry of a picker list (an action, a rule's trigger or condition...). <see cref="Spec"/> is what is stored; an entry with a
/// <see cref="ParamHint"/> takes text after the colon. <see cref="Name"/> is unique across its list, so it is also what a card shows.
/// <see cref="Keywords"/> are extra words the search matches (the shortcut, or what it is called elsewhere). <see cref="Options"/> are
/// the settings it offers (behind the cogwheel); <see cref="ParamLabel"/> names the text a ParamHint entry takes, in those settings.
/// <see cref="Unavailable"/> is why the entry can't be picked here (it is shown greyed with that reason); null = it can.
/// <see cref="ParamBrowse"/>: the text is a path, so its box has the Browse button for that kind of path. <see cref="ParamCheck"/> judges the text (one sentence beside its box, null = fine).
/// <see cref="AskFirst"/>: the entry is not complete without its settings (a command to run), so a picker that asks for them opens its settings page when it is picked.
/// <see cref="Remove"/>: the user may take the entry out of the list (it shows a remove cross). <see cref="Command"/>: the line is a button of its list (add a
/// program), not an action: picking it runs the command, which returns the entries to add. <see cref="Opened"/>: called each time a picker listing the entry opens.
/// <see cref="Remote"/> is the other PC the entry runs on (its row is marked "Other PCs · name"). <see cref="Category"/> is the entry's section in the Choose-the-action window (its pane 1); <see cref="Heading"/> is the small group heading it sits under in the
/// section's list, and <see cref="Place"/> the section's icon and order (read from the live page list; null in a list that has none). <see cref="RemoveText"/> names the
/// <see cref="Icon"/> is the entry's own glyph (a resource key of Styles/Brushes.axaml; null: its section's icon). <see cref="RemoveText"/> names the
/// <see cref="Family"/> and <see cref="FamilyRole"/> (from the master entry): the lists show the directions of a family as one row. <see cref="RemoveText"/> names the
/// remove button, <see cref="Renaming"/> lets the user rename it, <see cref="IsListed"/> false hides a list button for now, <see cref="Editor"/> holds the data the settings pane edits live (a custom action) and <see cref="Create"/> makes a new entry from the form's values (the entry is that form, not an action).
public sealed record PickChoice(string Spec, string Category, string Name, string? ParamHint = null, string Keywords = "",
    IReadOnlyList<ActionOption>? Options = null, string ParamLabel = "", string? Unavailable = null, bool AskFirst = false, PathKind? ParamBrowse = null,
    Action? Remove = null, Func<Visual, Task<IReadOnlyList<PickChoice>>>? Command = null, Action? Opened = null,
    string? Heading = null, ActionSection? Place = null, string RemoveText = "Remove", Renaming? Renaming = null, Func<bool>? IsListed = null,
    ActionEditor? Editor = null, Func<IReadOnlyDictionary<string, string>, PickChoice>? Create = null, string? Icon = null,
    ActionFamily? Family = null, string? FamilyRole = null, string? Tag = null, Shared.Services.RemotePcLabel? Remote = null,
    Func<string, string?>? ParamCheck = null, bool NamedByParam = false, string? Part = null, string? Detail = null, string? Value = null,
    string? Status = null, string? StatusTip = null, bool PicksAtOnce = false, string? PresetsHeading = null, Func<DialogMessage>? RemoveAsk = null)
{
    // Part: the muted heading over a run of sections in pane 1 ("Triggers", "States"); Detail: a muted fact after the name (the device);
    // Value: the live value as a neutral tag at the row's right; Status (tooltip StatusTip): the muted line under the settings title;
    // PicksAtOnce: a Command whose answer is used at once, without "Use this ..." (a side list's pick); PresetsHeading: a preset group's
    // action, listed again in the Presets section under this heading; RemoveAsk: the question Remove asks first.
    public bool HasOptions => Options is { Count: > 0 };

    /// The name a card shows for this entry with that text: the text itself for an entry <see cref="NamedByParam"/> (the keys it sends), else <see cref="Name"/>.
    public string ShownName(string parameter) => NamedByParam && parameter.Trim() is { Length: > 0 } text ? text : Name;

    /// True when a card shows the entry's text under its name (it is not already the name).
    public bool ShowsParameter(string parameter) => parameter.Length > 0 && !NamedByParam;

    /// True when a stored entry ("id" or "id:parameter", without options) is this choice.
    public bool Matches(string stored) => ParamHint is null ? Spec == stored : stored.StartsWith(Spec, StringComparison.Ordinal);

    /// The text after the colon of a stored entry that <see cref="Matches"/> this choice; empty for a choice that takes none.
    public string ParameterOf(string stored) => ParamHint is null ? string.Empty : stored[Spec.Length..];

    /// The stored entry ("id" or "id:parameter", without options) for this choice with that text.
    public string WithParameter(string parameter) => ParamHint is null ? Spec : Spec + parameter;

    /// Gathers choices into sections: the ones with a place (a page, or Power, Launch...) first in that order, then the rest in order of first
    /// appearance. A section takes the place and icon of its first choice that has one.
    public static List<PickGroup> Group(IEnumerable<PickChoice> choices) =>
        choices.GroupBy(c => c.Category)
            .Select(g => new PickGroup(g.Key, g.ToList(), g.Select(c => c.Place).FirstOrDefault(p => p is not null), g.First().Part))
            .OrderBy(g => g.Place?.Rank ?? int.MaxValue) // the sort is stable, so sections without a place keep their order
            .ToList();
}

/// One section of a picker list; <see cref="Place"/> is its icon and position when the list has them; <see cref="Part"/> the heading it sits under.
public sealed record PickGroup(string Name, IReadOnlyList<PickChoice> Choices, ActionSection? Place = null, string? Part = null);
