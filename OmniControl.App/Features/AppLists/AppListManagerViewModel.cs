namespace OmniControl.App.Features.AppLists;

/// One list in the manager's left pane: its name and how many apps it holds.
public sealed class AppListItem
{
    public AppListItem(AppList list) => List = list;

    public AppList List { get; }
    public string Name => List.Name;
    public string CountText => List.Apps.Count == 1 ? "1 app" : $"{List.Apps.Count} apps";
}

/// One app of the open list, shown as a chip with its remove cross.
public sealed class AppChipItem
{
    public AppChipItem(AppListEntry entry, Action<AppListEntry> remove)
    {
        Entry = entry;
        RemoveCommand = new RelayCommand(() => remove(entry));
    }

    public AppListEntry Entry { get; }
    public string Name => Entry.Name;
    public string Tip => $"{Entry.Program}.exe";
    public IRelayCommand RemoveCommand { get; }
}

/// The app lists manager window: the lists on the left (with their counts and "+ New list"), the
/// open list on the right (its name with Rename and Delete, its apps as chips, "+ Running app", "+ Installed app", "+ Browse...") and
/// "Used by": what is limited to this list, read from the scope models registered in <see cref="AppListStore"/>. Every change applies at
/// once. Deleting a list that is in use asks once; whatever used it then works in all apps.
public sealed partial class AppListManagerViewModel : ObservableObject, IDisposable
{
    private readonly AppListStore _store;
    private readonly Func<IReadOnlyList<InstalledApp>> _installed;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private bool _syncing;

    public AppListManagerViewModel(AppListStore store, Func<IReadOnlyList<InstalledApp>> installed, Func<DialogMessage, Task<bool>> ask)
    {
        _store = store;
        _installed = installed;
        _ask = ask;
        store.Changed += Reload;
        store.UsageChanged += ShowUsers;
        Reload();
    }

    public ObservableCollection<AppListItem> Lists { get; } = new();
    public ObservableCollection<AppChipItem> Apps { get; } = new();
    public ObservableCollection<string> UsedBy { get; } = new();
    public ObservableCollection<RunningProgram> Running { get; } = new();
    public ObservableCollection<InstalledApp> Installed { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(HasNoSelection), nameof(Heading))]
    private AppListItem? _selected;

    /// The rename box is showing; <see cref="EditText"/> is what is typed.
    [ObservableProperty] private bool _isRenaming;

    [ObservableProperty] private string _editText = string.Empty;

    /// A plain sentence under the list when something could not be added (an app already in it).
    [ObservableProperty] private string _message = string.Empty;

    public bool HasSelection => Selected is not null;
    public bool HasNoSelection => Selected is null;
    public bool HasNoApps => HasSelection && Apps.Count == 0;
    public bool HasUsers => UsedBy.Count > 0;
    public string Heading => Selected?.Name ?? string.Empty;
    public string UsedByTag => UsedBy.Count == 1 ? "used by 1" : $"used by {UsedBy.Count}";

    partial void OnSelectedChanged(AppListItem? value)
    {
        if (_syncing) return;
        IsRenaming = false;
        Message = string.Empty;
        ShowApps();
        ShowUsers();
    }

    // ---- lists ----

    [RelayCommand]
    private void NewList()
    {
        var made = _store.Create("New list");
        Selected = Lists.FirstOrDefault(l => l.List == made);
        BeginRename();
    }

    [RelayCommand]
    private void BeginRename()
    {
        if (Selected is null) return;
        EditText = Selected.Name;
        IsRenaming = true;
    }

    /// Takes the typed name (an empty or unchanged one changes nothing).
    public void CommitRename()
    {
        if (!IsRenaming) return;
        IsRenaming = false;
        if (Selected is { } open && EditText.Trim().Length > 0) _store.Rename(open.List, EditText);
    }

    public void CancelRename() => IsRenaming = false;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is not { } open) return;
        var users = _store.UsersOf(open.List.Id);
        if (users.Count > 0)
        {
            var text = $"“{open.Name}” is used by {users.Count} {(users.Count == 1 ? "setting" : "settings")}. They will work in all apps.";
            if (!await _ask(new DialogMessage("Delete this app list?", text, "Delete", RememberKey: Confirm.DeleteKey("app list"), RememberName: Confirm.DeleteName("app list")))) return;
        }
        _store.Delete(open.List);
    }

    // ---- apps ----

    private void Remove(AppListEntry entry)
    {
        if (Selected is { } open) _store.Remove(open.List, entry);
    }

    /// Adds a program to the open list; says so when it is already there.
    public void AddProgram(string program, string name)
    {
        if (Selected is not { } open) return;
        Message = _store.Add(open.List, program, name) ? string.Empty : "That app is already in the list.";
    }

    /// A program file the user browsed to: named as its file describes itself.
    public void AddFile(string path) => AddProgram(path, ProgramFile.NameOf(path));

    /// The "+ Running app" pop-down opened: the programs with a window now (looked up off the UI thread).
    public async Task RefreshRunningAsync()
    {
        var found = await Task.Run(ProgramIdentity.Running);
        Running.Clear();
        foreach (var program in found) Running.Add(program);
    }

    /// The "+ Installed app" pop-down opened: the installed programs the app knows.
    public async Task RefreshInstalledAsync()
    {
        var found = await Task.Run(_installed);
        Installed.Clear();
        foreach (var app in found) Installed.Add(app);
    }

    // ---- showing the store ----

    private void Reload()
    {
        var keep = Selected?.List.Id;
        _syncing = true;
        Lists.Clear();
        foreach (var list in _store.Lists) Lists.Add(new AppListItem(list));
        Selected = Lists.FirstOrDefault(l => l.List.Id == keep) ?? Lists.FirstOrDefault();
        _syncing = false;
        ShowApps();
        ShowUsers();
    }

    private void ShowApps()
    {
        Apps.Clear();
        if (Selected is { } open) foreach (var entry in open.List.Apps) Apps.Add(new AppChipItem(entry, Remove));
        OnPropertyChanged(nameof(HasNoApps));
    }

    private void ShowUsers()
    {
        UsedBy.Clear();
        if (Selected is { } open)
            foreach (var user in _store.UsersOf(open.List.Id))
                UsedBy.Add($"{user.Where} ({(user.Stored!.Mode == ScopeMode.Only ? "only in" : "all except")})");
        OnPropertyChanged(nameof(HasUsers));
        OnPropertyChanged(nameof(UsedByTag));
    }

    public void Dispose()
    {
        _store.Changed -= Reload;
        _store.UsageChanged -= ShowUsers;
    }
}
