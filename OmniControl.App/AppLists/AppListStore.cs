using OmniControl.Core.Engine;

namespace OmniControl.App.AppLists;

/// <summary>
/// The master list of app lists (single source of truth, AGENTS.md): every feature that limits something to apps names a list by id and
/// asks here. Read the live list, never a copy; <see cref="Changed"/> fires after every change. Save-on-change. The hook threads read
/// <see cref="Members"/>, an immutable snapshot swapped after each change.
/// </summary>
public sealed class AppListStore
{
    private readonly AppListSettings _settings;
    private readonly Action _save;
    private readonly List<IAppScopeUser> _users = new();
    private volatile IReadOnlyDictionary<string, HashSet<string>> _members = new Dictionary<string, HashSet<string>>();

    /// <param name="save">Stores the settings after every change (the settings' own file unless a test gives another).</param>
    public AppListStore(AppListSettings settings, Action? save = null)
    {
        _settings = settings;
        _save = save ?? settings.Save;
        TakeSnapshot();
    }

    /// <summary>Raised on the UI thread after any list was added, renamed, changed or deleted.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread when a user chose another scope (what uses which list changed, the lists did not).</summary>
    public event Action? UsageChanged;

    /// <summary>A user stored another scope: tells whoever shows who uses what.</summary>
    public void NotifyUsage() => UsageChanged?.Invoke();

    public IReadOnlyList<AppList> Lists => _settings.Lists;

    public AppList? Find(string id) => _settings.Lists.FirstOrDefault(l => l.Id == id);

    /// <summary>The program ids of a list (lower case), or null when there is no such list. Safe to call from any thread.</summary>
    public IReadOnlySet<string>? Members(string listId) => _members.GetValueOrDefault(listId);

    // ---- changing lists ----

    /// <summary>A new, empty list; <paramref name="name"/> is made unique when another list has it.</summary>
    public AppList Create(string name)
    {
        var list = new AppList { Name = UniqueName(name, null) };
        _settings.Lists.Add(list);
        Commit();
        return list;
    }

    public void Rename(AppList list, string name)
    {
        var wanted = UniqueName(name, list);
        if (wanted == list.Name) return;
        list.Name = wanted;
        Commit();
    }

    /// <summary>Deletes the list; whatever used it is put back to "All apps" first.</summary>
    public void Delete(AppList list)
    {
        foreach (var user in UsersOf(list.Id)) user.ResetToAll();
        _settings.Lists.Remove(list);
        Commit();
    }

    /// <summary>Adds the program (once, by its identity); false when it is already in the list or has no usable name.</summary>
    public bool Add(AppList list, string program, string name)
    {
        var id = ProgramIdentity.Normalize(program);
        if (id.Length == 0 || list.Apps.Any(a => a.Program == id)) return false;
        list.Apps.Add(new AppListEntry { Program = id, Name = name.Trim().Length > 0 ? name.Trim() : id });
        Commit();
        return true;
    }

    public void Remove(AppList list, AppListEntry entry)
    {
        if (!list.Apps.Remove(entry)) return;
        Commit();
    }

    // ---- who uses a list ----

    /// <summary>Registers a user (a scope model); it then shows in the manager and is reset when its list is deleted.</summary>
    public void Register(IAppScopeUser user) => _users.Add(user);

    public IReadOnlyList<IAppScopeUser> UsersOf(string listId) => _users.Where(u => u.Stored is { } s && s.ListId == listId && s.IsLimited).ToList();

    // ---- internals ----

    private string UniqueName(string wanted, AppList? except)
    {
        var name = wanted.Trim().Length > 0 ? wanted.Trim() : "New list";
        var taken = _settings.Lists.Where(l => l != except).Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (var n = 2; ; n++)
            if (!taken.Contains($"{name} {n}")) return $"{name} {n}";
    }

    private void Commit()
    {
        _save();
        TakeSnapshot();
        Changed?.Invoke();
    }

    private void TakeSnapshot()
    {
        var members = new Dictionary<string, HashSet<string>>();
        foreach (var list in _settings.Lists) members.TryAdd(list.Id, list.Apps.Select(a => a.Program).ToHashSet()); // a hand-edited file may repeat an id
        _members = members;
    }
}
