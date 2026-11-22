using System.ComponentModel;

namespace OmniControl.App.Features.AppLists;

/// The one model of an apps scope ("Apps: all except Games"), behind <c>Controls/AppScopeChip</c> and the menu a card's cogwheel opens.
/// It reads and writes the scope its owner stores (<c>read</c>, <c>write</c>: the owner saves and applies), registers itself as a user of
/// the lists (<see cref="AppListStore.Register"/>) and rebuilds its words when a list is renamed. A model made with a <c>group</c> is a
/// card's: its stored scope is null while it follows the group, the menu then offers "Same as the group", and it shows a tag only when it
/// differs from the group's. A model made with <c>excludes</c> is a list of apps something never works in ("Never in these apps: Games"):
/// its words are the list's name or "None", and its menu offers None and the lists. Lives as long as the page that made it.
public sealed partial class AppScopeModel : ObservableObject, IAppScopeUser
{
    private const string SameAsGroup = "Same as the group", AllAppsRow = "All apps", OnlyInRow = "Only in\u2026", AllExceptRow = "All except\u2026", ManageLists = "Manage app lists\u2026", NeverIn = "Never in", WorksIn = "Works in";
    private const string Prefix = "Apps: ", AllWords = "all", OnlyWord = "only", ExceptWords = "all except", NoneWord = "None", OutsideWords = "apps outside";

    /// What an apps scope says, for search: the chip's words and the rows of its menu.
    public static IReadOnlyList<string> MenuWords { get; } = new[] { Prefix + AllWords, Prefix + OnlyWord, Prefix + ExceptWords, NoneWord, SameAsGroup, AllAppsRow, OnlyInRow, AllExceptRow, ManageLists, WorksIn, NeverIn };

    private readonly AppListStore _store;
    private readonly Func<AppScopeSetting?> _read;
    private readonly Action<AppScopeSetting?> _write;
    private readonly Action _manage;
    private readonly AppScopeModel? _group;
    private readonly bool _excludes;

    [ObservableProperty] private string _menuTitle = string.Empty;

    /// Rows of the menu's current page, ending with "Manage app lists…".
    [ObservableProperty] private IReadOnlyList<MenuRowItem> _rows = Array.Empty<MenuRowItem>();

    public AppScopeModel(AppListStore store, string where, Func<AppScopeSetting?> read, Action<AppScopeSetting?> write, Action manage, AppScopeModel? group = null,
        bool excludes = false)
    {
        _excludes = excludes;
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

    /// The scope in force: the stored one, else the group's, else "all apps".
    public AppScopeSetting Effective => _read() ?? _group?.Effective ?? AppScopeSetting.All;

    /// The chip's words: "Apps: all", "Apps: only Games", "Apps: all except Games".
    public string Text => _excludes ? DescribeExcluded(Effective) : Prefix + Describe(Effective);

    /// The card's tag: its own scope, shown only while that differs from its group's.
    public bool ShowsTag => _group is not null && _read() is not null && Text != _group.Text;

    /// The menu should close (a choice was made, or the manager was asked for).
    public event Action? CloseRequested;

    /// Shows words and tag again (the stored scope changed outside, or a list was renamed).
    public void Refresh()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(ShowsTag));
    }

    public void ResetToAll() => Choose(AllChoice());

    // ---- the menu ----

    /// The menu was opened: its first page.
    public void OpenMenu() => ShowMain();

    private void OpenManager()
    {
        CloseRequested?.Invoke();
        _manage();
    }

    // What "all apps" is stored as: nothing for a group, an explicit "all" for a card (null there means "follow the group").
    private AppScopeSetting? AllChoice() => _group is null ? null : AppScopeSetting.All;

    private void ShowMain()
    {
        var stored = _read();
        var rows = new List<MenuRowItem>();
        if (_excludes)
        {
            MenuTitle = NeverIn;
            rows.Add(Row(NoneWord, string.Empty, stored is not { IsLimited: true }, () => Choose(null)));
            AddListRows(rows, ScopeMode.Except, stored);
        }
        else
        {
            MenuTitle = WorksIn;
            if (_group is not null) rows.Add(Row(SameAsGroup, Describe(_group.Effective), stored is null, () => Choose(null)));
            var isAll = _group is null ? stored is not { IsLimited: true } : stored is { IsLimited: false };
            rows.Add(Row(AllAppsRow, string.Empty, isAll, () => Choose(AllChoice())));
            rows.Add(PageRow(OnlyInRow, ScopeMode.Only, stored));
            rows.Add(PageRow(AllExceptRow, ScopeMode.Except, stored));
        }
        Show(rows);
    }

    private MenuRowItem PageRow(string label, ScopeMode mode, AppScopeSetting? stored)
    {
        var current = stored is { IsLimited: true } && stored.Mode == mode;
        var hint = current && _store.Find(stored!.ListId) is { } list ? list.Name : "pick a list";
        return Row(label, hint, current, () => ShowLists(mode)) with { OpensPage = true };
    }

    private void ShowLists(ScopeMode mode)
    {
        MenuTitle = mode == ScopeMode.Only ? "Works only in the apps of…" : "Works everywhere except the apps of…";
        var rows = new List<MenuRowItem> { Row("‹ Works in", string.Empty, false, ShowMain) with { OpensPage = true } };
        AddListRows(rows, mode, _read());
        Show(rows);
    }

    private void AddListRows(List<MenuRowItem> rows, ScopeMode mode, AppScopeSetting? stored)
    {
        foreach (var list in _store.Lists)
        {
            var scope = new AppScopeSetting(mode, list.Id);
            rows.Add(Row(list.Name, list.Apps.Count == 1 ? "1 app" : $"{list.Apps.Count} apps", stored == scope, () => Choose(scope)));
        }
        if (_store.Lists.Count == 0) rows.Add(MenuRowItem.Header("No app lists yet."));
    }

    private void Show(List<MenuRowItem> rows)
    {
        rows.Add(new MenuRowItem(ManageLists, string.Empty, false, string.Empty, new RelayCommand(OpenManager), RuleAbove: true, IsMuted: true));
        Rows = rows;
    }

    private static MenuRowItem Row(string label, string hint, bool current, Action run) =>
        new(label, string.Empty, current, string.Empty, new RelayCommand(run), Trailing: hint);

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

    // An exclusion list's words: what it leaves out ("Games"); a scope stored as "only" a list, from before, leaves out the apps outside it.
    private string DescribeExcluded(AppScopeSetting scope)
    {
        if (!scope.IsLimited || _store.Find(scope.ListId) is not { } list) return NoneWord;
        return scope.Mode == ScopeMode.Except ? list.Name : $"{OutsideWords} {list.Name}";
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
}
