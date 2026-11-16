using OmniControl.App.Hotkeys;

namespace OmniControl.App.Presets;

/// <summary>
/// Shortcuts that apply one preset. A preset with a shortcut has an OmniControl action in the master list ("Apply preset "Evening"", id
/// <c>preset.&lt;group&gt;.&lt;preset id&gt;</c>, under its page, its group and "Presets"), so the Shortcuts page, search and the mouse
/// pickers list it like any other action; its key is saved in hotkeys.json by that id. The action is made when a shortcut is given,
/// taken away when the shortcut is removed here, renamed with its preset and removed when its preset is deleted (its saved key stays, so
/// an undone delete brings it back). At start-up every preset with a saved key gets its action again. No UI.
/// </summary>
public sealed class PresetShortcuts : IDisposable
{
    private const string Prefix = "preset.";

    private readonly IActionRegistry _actions;
    private readonly PresetGroups _groups;
    private readonly Action<string> _report;
    private readonly Dictionary<string, PresetGroup> _mine = new(); // the actions made here, by action id
    private readonly List<(IPresetProvider Provider, Action Sync)> _watched = new();

    /// <summary>A preset's shortcut or its action changed (its group's id), here or on the Shortcuts page.</summary>
    public event Action<string>? Changed;

    /// <param name="report">Says why a preset applied by its shortcut could not be (the footer status line).</param>
    public PresetShortcuts(IActionRegistry actions, PresetGroups groups, Action<string> report)
    {
        (_actions, _groups, _report) = (actions, groups, report);
        foreach (var group in groups.All) Watch(group);
        groups.Registered += Watch;
    }

    public static string ActionId(string groupId, string presetId) => $"{Prefix}{groupId}.{presetId}";

    /// <summary>The shortcut that applies the preset ("Ctrl+Alt+1"), empty when it has none.</summary>
    public string ShortcutOf(PresetGroup group, string presetId) => _actions.Find(ActionId(group.Id, presetId))?.Gesture ?? string.Empty;

    /// <summary>Gives the preset this shortcut (empty = takes it away). Returns why it cannot be (the rule of <see cref="IActionRegistry.ProblemWith"/>), or null when done.</summary>
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

    /// <summary>Takes the preset's shortcut and its action away.</summary>
    public void Remove(PresetGroup group, string presetId)
    {
        var id = ActionId(group.Id, presetId);
        if (_actions.Find(id) is { } action) action.Gesture = string.Empty;
        Drop(id);
        Changed?.Invoke(group.Id);
    }

    private void Watch(PresetGroup group)
    {
        void Sync() => SyncActions(group);
        group.Provider.Changed += Sync;
        _watched.Add((group.Provider, Sync));
        Sync();
    }

    /// <summary>
    /// Brings the group's actions in line with its presets: an action for every preset with a saved key, the title following a rename, and
    /// no action for a preset that is gone.
    /// </summary>
    private void SyncActions(PresetGroup group)
    {
        var prefix = ActionId(group.Id, string.Empty);
        var ids = _actions.SavedWithShortcut(prefix).Concat(_mine.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))).Distinct().ToList();
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

    private static string TitleOf(PresetGroup group, PresetEntry entry) => $"{group.Provider.Words.ActionVerb} \"{entry.Name}\"";

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

    /// <summary>A key given or cleared on the Shortcuts page: the manager's card shows it.</summary>
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
        foreach (var (provider, sync) in _watched) provider.Changed -= sync;
        foreach (var id in _mine.Keys.ToList())
            if (_actions.Find(id) is { } action) action.Changed -= OnActionChanged;
    }
}
