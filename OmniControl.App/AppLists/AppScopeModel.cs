using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.AppLists;

/// <summary>
/// The one model of an apps scope ("Apps: all except Games"), behind <c>Controls/AppScopeChip</c> and the menu a card's cogwheel opens.
/// It reads and writes the scope its owner stores (<c>read</c>, <c>write</c>: the owner saves and applies), registers itself as a user of
/// the lists (<see cref="AppListStore.Register"/>) and rebuilds its words when a list is renamed. A model made with a <c>group</c> is a
/// card's: its stored scope is null while it follows the group, the menu then offers "Same as the group", and it shows a tag only when it
/// differs from the group's. Lives as long as the page that made it.
/// </summary>
public sealed partial class AppScopeModel : ObservableObject, IAppScopeUser
{
    private const string Prefix = "Apps: ", AllWords = "all", OnlyWord = "only", ExceptWords = "all except";

    private readonly AppListStore _store;
    private readonly Func<AppScopeSetting?> _read;
    private readonly Action<AppScopeSetting?> _write;
    private readonly Action _manage;
    private readonly AppScopeModel? _group;

    [ObservableProperty] private string _menuTitle = string.Empty;

    /// <summary>True while the page of lists has none: the menu says so and points at "Manage app lists…".</summary>
    [ObservableProperty] private bool _hasNoLists;

    public AppScopeModel(AppListStore store, string where, Func<AppScopeSetting?> read, Action<AppScopeSetting?> write, Action manage, AppScopeModel? group = null)
    {
        _store = store;
        Where = where;
        _read = read;
        _write = write;
        _manage = manage;
        _group = group;
        store.Changed += Refresh;
        if (group is not null) group.PropertyChanged += OnGroupChanged;
        store.Register(this);
    }

    public string Where { get; }

    public AppScopeSetting? Stored => _read();

    /// <summary>The scope in force: the stored one, else the group's, else "all apps".</summary>
    public AppScopeSetting Effective => _read() ?? _group?.Effective ?? AppScopeSetting.All;

    /// <summary>The chip's words: "Apps: all", "Apps: only Games", "Apps: all except Games".</summary>
    public string Text => Prefix + Describe(Effective);

    /// <summary>The card's tag: its own scope, shown only while that differs from its group's.</summary>
    public bool ShowsTag => _group is not null && _read() is not null && Text != _group.Text;

    /// <summary>Rows of the menu's current page.</summary>
    public ObservableCollection<ScopeMenuRow> Rows { get; } = new();

    /// <summary>The menu should close (a choice was made, or the manager was asked for).</summary>
    public event Action? CloseRequested;

    /// <summary>Shows words and tag again (the stored scope changed outside, or a list was renamed).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(ShowsTag));
    }

    public void ResetToAll() => Choose(AllChoice());

    // ---- the menu ----

    /// <summary>The menu was opened: its first page.</summary>
    public void OpenMenu() => ShowMain();

    public void OpenManager()
    {
        CloseRequested?.Invoke();
        _manage();
    }

    // What "all apps" is stored as: nothing for a group, an explicit "all" for a card (null there means "follow the group").
    private AppScopeSetting? AllChoice() => _group is null ? null : AppScopeSetting.All;

    private void ShowMain()
    {
        MenuTitle = "Works in";
        HasNoLists = false;
        var stored = _read();
        Rows.Clear();
        if (_group is not null) Rows.Add(new ScopeMenuRow("Same as the group", Describe(_group.Effective), stored is null, false, () => Choose(null)));
        var isAll = _group is null ? stored is not { IsLimited: true } : stored is { IsLimited: false };
        Rows.Add(new ScopeMenuRow("All apps", string.Empty, isAll, false, () => Choose(AllChoice())));
        Rows.Add(PageRow("Only in…", ScopeMode.Only, stored));
        Rows.Add(PageRow("All except…", ScopeMode.Except, stored));
    }

    private ScopeMenuRow PageRow(string label, ScopeMode mode, AppScopeSetting? stored)
    {
        var current = stored is { IsLimited: true } && stored.Mode == mode;
        var hint = current && _store.Find(stored!.ListId) is { } list ? list.Name : "pick a list";
        return new ScopeMenuRow(label, hint, current, true, () => ShowLists(mode));
    }

    private void ShowLists(ScopeMode mode)
    {
        MenuTitle = mode == ScopeMode.Only ? "Works only in the apps of…" : "Works everywhere except the apps of…";
        var stored = _read();
        Rows.Clear();
        Rows.Add(new ScopeMenuRow("‹ Works in", string.Empty, false, false, ShowMain));
        foreach (var list in _store.Lists)
        {
            var scope = new AppScopeSetting(mode, list.Id);
            Rows.Add(new ScopeMenuRow(list.Name, list.Apps.Count == 1 ? "1 app" : $"{list.Apps.Count} apps", stored == scope, false, () => Choose(scope)));
        }
        HasNoLists = _store.Lists.Count == 0;
    }

    private void Choose(AppScopeSetting? scope)
    {
        _write(scope);
        Refresh();
        _store.NotifyUsage();
        CloseRequested?.Invoke();
    }

    // ---- words ----

    private string Describe(AppScopeSetting scope)
    {
        if (!scope.IsLimited || _store.Find(scope.ListId) is not { } list) return AllWords;
        return scope.Mode == ScopeMode.Only ? $"{OnlyWord} {list.Name}" : $"{ExceptWords} {list.Name}";
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
}
