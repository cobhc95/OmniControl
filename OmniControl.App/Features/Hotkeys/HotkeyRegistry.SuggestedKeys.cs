namespace OmniControl.App.Features.Hotkeys;

/// The suggested-keys switch of a family of actions (<see cref="HotkeyAction.SuggestedKeySet"/>; drawn by <see cref="SuggestedKeysSwitch"/>).
/// On = the family's actions hold their suggested keys; the user's own keys for them are remembered and put back exactly when it goes off.
/// The state is one entry of the saved bindings (<see cref="MetaId"/>, no keys of its own), so it is kept per shortcut set like the keys are.
/// Any hand edit of a member's keys turns it off (see <see cref="TurnOffByHand"/>, called from the one place keys change, <c>OnChanged</c>).
public sealed partial class HotkeyRegistry
{
    private const string MetaPrefix = "suggested-keys:";
    private const string OnOption = "on";
    private const string OwnPrefix = "own:";
    private const string KeySeparator = "\n";

    // True while this class moves the members' keys itself, so that is not mistaken for a hand edit.
    private bool _applying;

    private static string MetaId(string setId) => MetaPrefix + setId;

    /// The actions of the set that have a suggested key, in registration order.
    public IReadOnlyList<HotkeyAction> SuggestedMembers(string setId) =>
        Actions.Where(a => a.SuggestedKeySet == setId && a.SuggestedKey is not null).ToList();

    public bool SuggestedKeysOn(string setId) => _stored.TryGetValue(MetaId(setId), out var meta) && meta.Options?.GetValueOrDefault(OnOption) == "1";

    /// The action outside the set that holds this member's suggested key (so the member went without it), or null.
    public HotkeyAction? SuggestedKeyHeldBy(HotkeyAction member) =>
        member.SuggestedKey is { } key && !member.Keys.Contains(key)
            ? Actions.FirstOrDefault(a => a.Id != member.Id && a.SuggestedKeySet != member.SuggestedKeySet && a.Keys.Contains(key))
            : null;

    /// Switches the set on (remembers the members' keys, gives each its suggested key unless another action has it) or off (puts the remembered keys back,
    /// each unless another action has taken it since), with one save.
    public void SetSuggestedKeys(string setId, bool on)
    {
        var members = SuggestedMembers(setId);
        if (members.Count == 0 || on == SuggestedKeysOn(setId)) return;
        var skipped = new List<string>();
        if (on)
        {
            WriteMeta(setId, true, members.ToDictionary(m => m.Id, m => m.Keys.ToList()));
            Place(members, m => new[] { m.SuggestedKey! }, skipped);
        }
        else
        {
            var own = members.ToDictionary(m => m.Id, m => OwnKeysOf(setId, m.Id));
            WriteMeta(setId, false, null);
            Place(members, m => own[m.Id], skipped);
            if (skipped.Count > 0) _report($"{string.Join(", ", skipped)} could not get back an old key: another action has it now.");
        }
        Save();
        Rebind();
    }

    /// Gives each member exactly the keys <paramref name="keysFor"/> names, dropping (and naming in <paramref name="skipped"/>) any another action has.
    private void Place(IReadOnlyList<HotkeyAction> members, Func<HotkeyAction, IEnumerable<string>> keysFor, List<string> skipped)
    {
        _applying = true;
        try
        {
            foreach (var member in members) member.SetKeys(Array.Empty<string>());
            foreach (var member in members)
            {
                var wanted = keysFor(member).ToList();
                var usable = wanted.Where(k => ConflictOf(k, member.Id, Actions) is null).ToList();
                if (usable.Count < wanted.Count) skipped.Add(member.Title);
                member.SetKeys(usable);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    /// True when the member holds its suggested key, or has none because another action has it.
    private bool IsAsSuggested(HotkeyAction member)
    {
        var keys = member.Keys;
        if (keys.Count == 1) return keys[0] == member.SuggestedKey;
        return keys.Count == 0 && member.SuggestedKey is { } key && ConflictOf(key, member.Id, Actions) is not null;
    }

    /// A member's keys were changed by hand while the set is on: the set goes off and what is there now is the user's own.
    private void TurnOffByHand(HotkeyAction changed)
    {
        if (_applying || changed.SuggestedKeySet is not { } setId || !SuggestedKeysOn(setId) || IsAsSuggested(changed)) return;
        WriteMeta(setId, false, null);
    }

    /// Decides the sets that have no state yet and keeps the others honest, after actions were registered or bindings were put in force:
    /// a fresh install starts with a set on, anyone with bindings already starts with it off, and a set marked on whose members no longer hold their keys is off.
    /// True when something changed that needs saving.
    private bool SettleSuggestedKeys()
    {
        var changed = false;
        foreach (var setId in Actions.Select(a => a.SuggestedKeySet).OfType<string>().Distinct().ToList())
        {
            var members = SuggestedMembers(setId);
            if (_stored.ContainsKey(MetaId(setId)))
            {
                if (!SuggestedKeysOn(setId) || members.All(IsAsSuggested)) continue;
                WriteMeta(setId, false, null);
                changed = true;
            }
            else if (_fresh && members.All(m => m.Keys.Count == 0)) SetSuggestedKeys(setId, true);
            else
            {
                WriteMeta(setId, false, null);
                changed = true;
            }
        }
        return changed;
    }

    /// Keys just put in place (Restore defaults, an undo) that are exactly a set's suggested keys mean the set is on, as on a fresh install; nothing
    /// is remembered as the user's own then, because the keys that were there are gone.
    private void AdoptSuggestedKeys()
    {
        foreach (var setId in Actions.Select(a => a.SuggestedKeySet).OfType<string>().Distinct().ToList())
        {
            var members = SuggestedMembers(setId);
            if (!SuggestedKeysOn(setId) && members.Any(m => m.Keys.Count > 0) && members.All(IsAsSuggested))
                WriteMeta(setId, true, null);
        }
    }

    /// What "Restore defaults" puts in place: every action on its default key, then each suggested-keys set on with its suggested keys (each
    /// unless another action has it), the same as a fresh install.
    public Dictionary<string, StoredHotkey> DefaultBindings()
    {
        var all = HotkeyDefaults.Of(ExportAll(), DefaultKeyOf);
        var taken = all.Values.SelectMany(s => s.Keys).ToHashSet();
        foreach (var member in Actions.Select(a => a.SuggestedKeySet).OfType<string>().Distinct().SelectMany(SuggestedMembers))
        {
            var key = member.SuggestedKey!;
            if (all[member.Id].Keys.Any() || taken.Contains(key) || HotkeyService.ProblemWith(key) is not null || ReservedKeys.ProblemWith(key) is not null) continue;
            all[member.Id] = HotkeyDefaults.For(key);
            taken.Add(key);
        }
        return all;
    }

    private void WriteMeta(string setId, bool on, IReadOnlyDictionary<string, List<string>>? own)
    {
        var options = new Dictionary<string, string> { [OnOption] = on ? "1" : "0" };
        foreach (var (id, keys) in own ?? new Dictionary<string, List<string>>())
            if (keys.Count > 0) options[OwnPrefix + id] = string.Join(KeySeparator, keys);
        _stored[MetaId(setId)] = new StoredHotkey(string.Empty, false, new List<string>(), null, options);
    }

    private List<string> OwnKeysOf(string setId, string actionId) =>
        _stored.TryGetValue(MetaId(setId), out var meta) && meta.Options?.GetValueOrDefault(OwnPrefix + actionId) is { Length: > 0 } text
            ? text.Split(KeySeparator).ToList()
            : new List<string>();
}
