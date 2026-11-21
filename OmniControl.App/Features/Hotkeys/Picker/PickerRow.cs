using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Icons;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// What the picker returns: the chosen entry, the values of its settings, and (for an entry that takes text, such as a shortcut) that text; null when the entry takes none. ApplyToAll is true when the "apply to every gesture" switch was left on.
public sealed record PickResult(PickChoice Choice, IReadOnlyDictionary<string, string> Values, string? Parameter = null, bool ApplyToAll = false)
{
    /// The pick as a rule stores it: the entry with its text and the values of its settings.
    public string Stored => MouseAction.Compose(Choice.WithParameter(Parameter?.Trim() ?? string.Empty), Choice.Options, Values);
}

/// One entry of the sections pane: its name and its icon (a page's own; null where the list has none); <paramref name="IsFavourites"/> draws the star.
public sealed record SectionRow(string Name, string? Icon, bool IsFavourites = false)
{
    public bool HasIcon => Icon is not null;
}

/// One line of the list: an entry (its text, and while searching or in Recent the section it belongs to, and why it can't be picked when it can't) or a group heading, which has no choice and is never highlighted.
public sealed class PickerRow
{
    public PickChoice? Choice { get; }
    public string Text { get; }
    public string? Hint { get; }

    /// The entry's small fact tag ("active"), or null.
    public string? Tag => Choice?.Tag;
    public bool HasTag => Tag is not null;

    /// The other PC the entry runs on (drawn as "Other PCs · name"), or null; its section then replaces the hint.
    public Shared.Services.RemotePcLabel? Remote => Choice?.Remote;
    public bool ShowsHint => Remote is null;
    public string? Unavailable => Members is { } members ? (members.All(m => m.Unavailable is not null) ? members[0].Unavailable : null) : Choice?.Unavailable; // a family is greyed only when every direction is
    public bool IsAvailable => Unavailable is null;

    public bool IsHeading => Choice is null;

    /// The directions of a family, in order, when this line stands for one (<see cref="Choice"/> is the first of them); null for a single entry.
    public IReadOnlyList<PickChoice>? Members { get; }

    /// The family's key (section, group and id), or null for a single entry or a heading.
    public string? FamilyKey { get; }

    /// A family's keys in a few words, one per direction ("Up Ctrl+F7 · Down no key"; the directions alone while none has a key); empty for a single entry.
    public string Summary { get; } = string.Empty;
    public bool HasSummary => Summary.Length > 0;

    private readonly IReadOnlyList<string> _keys;
    private IReadOnlyList<IReadOnlyList<KeycapPart>>? _keycaps;
    private IconImage? _icon;
    private bool _iconRead;

    /// The entry's own glyph, or its section's icon when it has none; null for a heading. Read when the row is first drawn.
    public IconImage? Icon
    {
        get
        {
            if (_iconRead) return _icon;
            _iconRead = true;
            return _icon = Choice is not { Command: null } ? null : IconResources.Of(Choice.Icon, Choice.Place?.Icon);
        }
    }

    public bool HasIcon => Icon is not null;

    /// The line is a button of its list (add a program), not an entry.
    public bool IsCommand => Choice?.Command is not null;
    public bool IsEntry => Choice is { Command: null };

    /// The entry's keys as keycaps, one row of parts per key (the Actions and hotkeys window); empty when it has none. Made when the
    /// row is first drawn, so a long list only makes the keycaps of the rows on screen.
    public IReadOnlyList<IReadOnlyList<KeycapPart>> Keycaps => _keycaps ??= _keys.Select(k => Hotkeys.Keycaps.From(k, string.Empty)).ToList();

    /// The entry can have keys but has none: the row says "no key".
    public bool ShowsNoKey { get; }

    /// The entry is in the panel the picker was opened from (the tray flyout): the row says so in place of "no key".
    public bool InPanel { get; }

    /// The row has a favourite star (an entry of a list of actions); <see cref="IsFavourite"/> fills it.
    public bool CanStar { get; init; }
    public bool IsFavourite { get; init; }
    public string StarTip => StarTipFor(IsFavourite);

    /// The row repeats a starred entry under the Favourites heading at the top of its section.
    public bool IsCopy { get; init; }

    /// What a favourite star does when clicked.
    public static string StarTipFor(bool on) => on ? "Remove from favourites" : "Add to favourites";

    /// A line for a family: its title, its directions and their keys in <paramref name="summary"/>.
    public PickerRow(string familyKey, string title, IReadOnlyList<PickChoice> members, string? hint, string summary, bool inPanel)
        : this(members[0], title, hint, null, inPanel)
    {
        (FamilyKey, Members, Summary) = (familyKey, members, summary);
    }

    public PickerRow(PickChoice choice, string text, string? hint, IReadOnlyList<string>? keys = null, bool inPanel = false)
    {
        Choice = choice;
        Text = text;
        Hint = hint;
        _keys = keys ?? Array.Empty<string>();
        InPanel = inPanel;
        ShowsNoKey = keys is { Count: 0 } && !inPanel;
    }

    private PickerRow(string heading)
    {
        Text = heading;
        _keys = Array.Empty<string>();
    }

    public static PickerRow HeadingOf(string heading) => new(heading);

    // A heading's key: its text behind a mark no entry's spec starts with; a favourite's copy: its spec behind another such mark.
    private const string HeadingKey = "\u0001heading:", CopyKey = "\u0001favourite:";

    /// What makes two rows the same line of the list (<see cref="ListDiff"/>): the entry (or its favourite's copy), or a heading's text.
    public static object KeyOf(PickerRow row) => row.Choice is { } choice ? (row.IsCopy ? CopyKey : string.Empty) + choice.Spec : HeadingKey + row.Text;

    /// True when the two rows draw the same (the shown row is kept as it is).
    public static bool Same(PickerRow shown, PickerRow wanted) =>
        ReferenceEquals(shown.Choice, wanted.Choice) && shown.Text == wanted.Text && shown.Hint == wanted.Hint && shown.Tag == wanted.Tag
        && shown.CanStar == wanted.CanStar && shown.IsFavourite == wanted.IsFavourite
        && shown.InPanel == wanted.InPanel && shown.ShowsNoKey == wanted.ShowsNoKey && shown._keys.SequenceEqual(wanted._keys)
        && shown.Summary == wanted.Summary && (shown.Members ?? Array.Empty<PickChoice>()).SequenceEqual(wanted.Members ?? Array.Empty<PickChoice>());
}

