using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Shared.Presets;

/// Shortcuts that apply one preset. A preset with a shortcut has an OmniControl action in the master list ("Apply preset "Evening"", id
/// <c>preset.&lt;group&gt;.&lt;preset id&gt;</c>, under its page, its group and "Presets"), so the Shortcuts page, search and the mouse
/// pickers list it like any other action; its key is saved in hotkeys.json by that id. The action is made when a shortcut is given,
/// taken away when the shortcut is removed here, renamed with its preset and removed when its preset is deleted (its saved key stays, so
/// an undone delete brings it back). At start-up every preset with a saved key gets its action again. No UI.
/// A preset with triggers (<see cref="IPresetSwitch.Keep"/>) keeps its action without a key, so its rule's step (and a put back) can run it.
public sealed class PresetShortcuts : IPresetSwitch, IDisposable
{
    private HashSet<string> _kept = new();
    private const string Prefix = "preset.";

    private readonly IActionRegistry _actions;
    private readonly PresetGroups _groups;
    private readonly Action<string> _report;
    private readonly Dictionary<string, PresetGroup> _mine = new(); // the actions made here, by action id
    private readonly List<(IPresetProvider Provider, Action Sync)> _watched = new();

    /// A preset's shortcut or its action changed (its group's id), here or on the Shortcuts page.
    public event Action<string>? Changed;

    /// <param name="report">Says why a preset applied by its shortcut could not be (the footer status line).</param>
    public PresetShortcuts(IActionRegistry actions, PresetGroups groups, Action<string> report)
    {
        (_actions, _groups, _report) = (actions, groups, report);
        foreach (var group in groups.All) Watch(group);
        groups.Registered += Watch;
        groups.Removed += Unwatch;
    }

    public static string ActionId(string groupId, string presetId) => $"{Prefix}{groupId}.{presetId}";

    /// True for the id of a preset's apply action (whatever its group).
    public static bool IsApplyId(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);

    /// The shortcut that applies the preset ("Ctrl+Alt+1"), empty when it has none.
    public string ShortcutOf(PresetGroup group, string presetId) => _actions.Find(ActionId(group.Id, presetId))?.Gesture ?? string.Empty;

    /// Gives the preset this shortcut (empty = takes it away). Returns why it cannot be (the rule of <see cref="IActionRegistry.ProblemWith"/>), or null when done.
    public string? Set(PresetGroup group, PresetEntry entry, string gesture)
    {
        if (gesture.Length == 0)
        {
            Remove(group, entry.Id);
            return null;
        }
        var id = ActionId(group.Id, entry.Id);
        if (_actions.ProblemWith(gesture, id) is { } problem) return problem;
        var action = _actions.Find(id) ?? Make(group, entry);
        action.Gesture = gesture; // the master list saves it; OnActionChanged tells the card
        return null;
    }

    /// Takes the preset's shortcut and its action away.
    public void Remove(PresetGroup group, string presetId)
    {
        var id = ActionId(group.Id, presetId);
        if (_actions.Find(id) is { } action) action.Gesture = string.Empty;
        if (!_kept.Contains(id)) Drop(id);
        Changed?.Invoke(group.Id);
    }

    public string? InUse(string preset) =>
        GroupOf(preset) is { } group && group.Provider.Entries.FirstOrDefault(e => e.InUse) is { } entry ? ActionId(group.Id, entry.Id) : null;

    public bool? Exists(string preset) => GroupOf(preset) is { } group ? group.Provider.Find(preset[ActionId(group.Id, string.Empty).Length..]) is not null : null;

    public void Keep(IEnumerable<string> presets)
    {
        var kept = presets.ToHashSet();
        if (kept.SetEquals(_kept)) return;
        _kept = kept;
        foreach (var id in _mine.Keys.Where(id => !kept.Contains(id) && _actions.Find(id) is { Gesture.Length: 0 }).ToList()) Drop(id);
        foreach (var group in _groups.All) SyncActions(group);
    }

    // The longest group prefix wins, so a group whose id starts with another's is never mistaken for it.
    public PresetGroup? GroupOf(string preset) => _groups.All.Where(g => preset.StartsWith(ActionId(g.Id, string.Empty), StringComparison.Ordinal))
        .MaxBy(g => g.Id.Length);

    private void Watch(PresetGroup group)
    {
        void Sync() => SyncActions(group);
        group.Provider.Changed += Sync;
        _watched.Add((group.Provider, Sync));
        Sync();
    }

    /// The group was taken out: its preset actions go with it (their saved keys stay, so they come back with it).
    private void Unwatch(PresetGroup group)
    {
        foreach (var (provider, sync) in _watched.Where(w => w.Provider == group.Provider).ToList())
        {
            provider.Changed -= sync;
            _watched.Remove((provider, sync));
        }
        foreach (var id in _mine.Where(m => m.Value == group).Select(m => m.Key).ToList()) Drop(id);
    }

    /// Brings the group's actions in line with its presets: an action for every preset with a saved key, the title following a rename, and
    /// no action for a preset that is gone.
    private void SyncActions(PresetGroup group)
    {
        var prefix = ActionId(group.Id, string.Empty);
        var ids = _actions.SavedWithShortcut(prefix).Concat(_mine.Keys.Concat(_kept).Where(k => k.StartsWith(prefix, StringComparison.Ordinal))).Distinct().ToList();
        var changed = false;
        foreach (var id in ids)
        {
            var entry = group.Provider.Find(id[prefix.Length..]);
            var made = _mine.ContainsKey(id) ? _actions.Find(id) : null;
            if (entry is not null && made?.Title == TitleOf(group, entry)) continue;
            if (made is not null) Drop(id);
            if (entry is not null) Make(group, entry); // hotkeys.json gives it its saved key
            changed = true;
        }
        if (changed) Changed?.Invoke(group.Id);
    }

    internal static string TitleOf(PresetGroup group, PresetEntry entry) => $"{group.Provider.Words.ActionVerb} \"{entry.Name}\"";

    private HotkeyAction Make(PresetGroup group, PresetEntry entry)
    {
        var presetId = entry.Id;
        var action = new HotkeyAction(ActionId(group.Id, presetId), TitleOf(group, entry), group.Section,
            $"preset apply switch {group.Title} {entry.Name}", _ => Run(group, presetId), group: group.Title, subgroup: PresetGroups.Subgroup) { CoveredByMousePicker = group.MovesLayer };
        _actions.Register(action);
        _mine[action.Id] = group;
        action.Changed += OnActionChanged;
        return action;
    }

    private void Drop(string id)
    {
        if (_actions.Find(id) is { } action) action.Changed -= OnActionChanged;
        _mine.Remove(id);
        _actions.Unregister(id);
    }

    /// A key given or cleared on the Shortcuts page: the manager's card shows it.
    private void OnActionChanged(HotkeyAction action)
    {
        if (_mine.TryGetValue(action.Id, out var group)) Changed?.Invoke(group.Id);
    }

    private void Run(PresetGroup group, string presetId)
    {
        if (group.Provider.Apply(presetId) is { } problem) _report(problem);
    }

    public void Dispose()
    {
        _groups.Registered -= Watch;
        _groups.Removed -= Unwatch;
        foreach (var (provider, sync) in _watched) provider.Changed -= sync;
        foreach (var id in _mine.Keys.ToList())
            if (_actions.Find(id) is { } action) action.Changed -= OnActionChanged;
    }
}
