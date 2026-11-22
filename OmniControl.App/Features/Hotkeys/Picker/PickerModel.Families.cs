using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// Families in the list: the directions of one thing (<see cref="ActionFamily"/>: Pointer speed up and down)
/// are ONE line of the Actions and Hotkeys window and of every pick list. Each direction is still its own entry with its own id, so the line only
/// remembers which direction is shown in the settings pane: the Actions window shows one key block per direction, a pick shows a "Direction"
/// setting at the top that chooses which one is used. A search finds each direction by its own name and shows the family's line with that one chosen.
public sealed partial class PickerModel
{
    private const string DirectionKey = "$direction"; // the pick's choice of direction, kept apart from the entry's settings

    private readonly Dictionary<string, string> _memberPicked = new(); // family key -> Spec of the direction shown in the settings pane

    /// The family's key: its section, group heading and id (so two features that reuse an id never merge); null for an entry that is alone.
    private static string? FamilyKeyOf(PickChoice choice) =>
        choice.Family is { } family && choice.Command is null ? $"{choice.Category}\u0001{choice.Heading}\u0001{family.Id}" : null;

    /// The direction of a line that the settings pane shows: the one picked (by a search, the card's current action or the Direction setting), else the first.
    private PickChoice? MemberOf(PickerRow row)
    {
        if (row.Members is not { } members) return row.Choice;
        return members.FirstOrDefault(m => m.Spec == _memberPicked.GetValueOrDefault(row.FamilyKey!)) ?? members[0];
    }

    /// The line holding <paramref name="choice"/> (a single entry or one direction of a family, which then becomes the one shown); -1 when it is not there.
    private int RowOf(IReadOnlyList<PickerRow> rows, PickChoice choice)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Choice == choice) return i;
            if (rows[i].Members?.Contains(choice) != true) continue;
            _memberPicked[rows[i].FamilyKey!] = choice.Spec;
            return i;
        }
        return -1;
    }

    /// One line per entry, and one per family (at the place of its first direction in <paramref name="candidates"/>). With <paramref name="preselect"/>
    /// (a search's matches, Recent) the direction that came first in the list is the one shown.
    private List<PickerRow> Rows(IEnumerable<PickChoice> candidates, Func<PickChoice, string?> hintOf, bool preselect, bool copy = false)
    {
        var rows = new List<PickerRow>();
        var seen = new HashSet<string>();
        Dictionary<string, List<PickChoice>>? families = null;
        foreach (var choice in candidates)
        {
            var key = FamilyKeyOf(choice);
            if (key is not null) families ??= AllFamilies();
            if (key is null || families![key].Count < 2)
            {
                rows.Add(Row(choice, hintOf(choice), copy));
                continue;
            }
            if (!seen.Add(key)) continue;
            if (preselect) _memberPicked[key] = choice.Spec;
            rows.Add(FamilyRow(key, families[key], hintOf(choice), copy));
        }
        return rows;
    }

    /// Every direction of every family, read from the whole list (never from a filtered part), in list order.
    private Dictionary<string, List<PickChoice>> AllFamilies() =>
        _groups.SelectMany(g => g.Choices).Where(c => FamilyKeyOf(c) is not null).GroupBy(c => FamilyKeyOf(c)!).ToDictionary(g => g.Key, g => g.ToList());

    private PickerRow FamilyRow(string key, IReadOnlyList<PickChoice> members, string? hint, bool copy) =>
        new(key, members[0].Family!.Title, members, hint, SummaryOf(members), members.Any(IsInPanel))
        {
            CanStar = CanStar(members[0]), IsFavourite = members.Any(IsFavourite), IsCopy = copy,
        };

    private string SummaryOf(IReadOnlyList<PickChoice> members)
    {
        var parts = members.Select(m => (Role: RoleOf(m), Keys: KeysOf(m))).ToList();
        if (parts.All(p => p.Keys is not { Count: > 0 })) return string.Join(" · ", parts.Select(p => p.Role));
        return string.Join(" · ", parts.Select(p => $"{p.Role} {(p.Keys is { Count: > 0 } keys ? string.Join(", ", keys) : "no key")}"));
    }

    private static string RoleOf(PickChoice choice) => choice.FamilyRole ?? choice.Name;

    /// The title the settings pane shows for the highlighted entry: a family's name, else the entry's.
    private string TitleOf(PickChoice choice) => ActiveRow is { Members: not null } row ? row.Text : choice.Name;

    /// A pick of one direction of a family: the "Direction" setting at the top of its settings.
    private void AddDirectionRow(PickChoice choice)
    {
        if (!IsPicking || ActiveRow is not { Members: { } members }) return;
        _allOptions.Insert(0, new OptionItem(ActionOption.Choice(DirectionKey, "Direction", members.Select(RoleOf).ToList()), RoleOf(choice)));
    }

    /// The Direction setting was changed: the settings pane shows that direction's settings (the pick itself waits for "Use ...").
    private void PickDirection(string role)
    {
        if (ActiveRow is not { Members: { } members, FamilyKey: { } key }) return;
        if (members.FirstOrDefault(m => RoleOf(m) == role) is not { } member) return;
        _memberPicked[key] = member.Spec;
        SyncSettings();
    }

    /// The master actions of the highlighted family's directions: what a setting they share (how far a level moves) is written to as well.
    private IEnumerable<HotkeyAction> FamilyActions() =>
        ActiveRow is { Members: { } members } && _manage is { } keys ? members.Select(keys.ActionOf).OfType<HotkeyAction>() : Array.Empty<HotkeyAction>();

    /// The directions whose keys the settings pane shows: all of a family in the Actions window, otherwise just the highlighted entry.
    private IEnumerable<PickChoice> HotkeyMembers(PickChoice active) =>
        IsManaging && ActiveRow is { Members: { } members } ? members : new[] { active };
}
