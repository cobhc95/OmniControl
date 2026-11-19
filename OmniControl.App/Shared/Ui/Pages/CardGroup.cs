using System.Windows.Input;

namespace OmniControl.App.Shared.Ui.Pages;

/// A collapsible panel of cards. Collapsed by default unless <see cref="ExpandedByDefault"/>; what the user opened or closed is remembered between runs,
/// unless the "Groups when a page opens" setting (<see cref="GroupsOpen"/>) says always open or always collapsed.
/// <see cref="Key"/> must be unique across the program (for example "OSD/Customize").
public class CardGroup : ObservableObject
{
    private bool _isExpanded;
    private string _summary;
    private bool _defaultOpen;
    private ICommand? _restoreCommand;

    public string Key { get; }
    public string Name { get; }

    /// Facts about the whole group that are not on its cards (what it talks to, its limits), shown as the header's tooltip; null: none.
    public string? Tip { get; init; }

    /// A library icon id drawn before the name on the header (a group that has one, such as the Other PCs cards); null: none.
    public string? Icon { get; init; }

    /// True: the summary follows the name on the same line (grey) instead of sitting at the right edge.
    public bool SummaryBesideName { get; init; }

    /// Puts the group's settings back to their defaults; set: the header gets the shared "Restore defaults" pill (<see cref="GroupRestorePill"/>), null: none.
    public Action? Restore { get; init; }

    /// The words on the pill.
    public string RestoreText { get; init; } = "Restore defaults";

    /// What the pill's tooltip says it restores.
    public string? RestoreTip { get; init; }

    /// A small fact (icon and words) at the header's right instead of the summary, or null.
    public GroupBadge? Badge
    {
        get => _badge;
        set
        {
            if (SetProperty(ref _badge, value)) OnPropertyChanged(nameof(HasBadge));
        }
    }

    public bool HasBadge => _badge is not null;

    private GroupBadge? _badge;

    /// True: the summary is shown only while the group is closed (a list of what is inside, which the open group shows itself).
    public bool SummaryWhenClosed { get; init; }

    public bool HasRestore => Restore is not null;

    public ICommand? RestoreCommand => Restore is null ? null : _restoreCommand ??= new RelayCommand(Restore);

    /// The short text at the right of the header (a count); a page may update it.
    public string Summary
    {
        get => _summary;
        set
        {
            if (SetProperty(ref _summary, value)) OnPropertyChanged(nameof(ShownSummary));
        }
    }

    /// What the header shows: <see cref="Summary"/>, or nothing while an open group has <see cref="SummaryWhenClosed"/>.
    public string ShownSummary => SummaryWhenClosed && _isExpanded ? string.Empty : _summary;

    /// Starts open unless the user closed it themselves (a closed group is remembered in <see cref="AppSettings.CollapsedGroups"/>).
    public bool ExpandedByDefault
    {
        init { _defaultOpen = value; _isExpanded = StartsOpen(); }
    }

    public CardGroup(string key, string name, string summary)
    {
        Key = key;
        Name = name;
        _summary = summary;
        _isExpanded = StartsOpen();
        var weak = new WeakReference<CardGroup>(this);
        Action? follow = null;
        follow = () =>
        {
            if (weak.TryGetTarget(out var group)) group.ApplySetting();
            else GroupsOpen.Changed -= follow;
        };
        GroupsOpen.Changed += follow; // weak: groups come and go (a monitor's) and nothing disposes them
    }

    /// The state a group opens in under the current setting (the single place that reads it).
    private bool StartsOpen()
    {
        var settings = AppSettings.Current;
        return GroupsOpen.Current switch
        {
            GroupsOpenChoice.AlwaysOpen => true,
            GroupsOpenChoice.AlwaysCollapsed => false,
            _ => settings.ExpandedGroups.Contains(Key) || (_defaultOpen && !settings.CollapsedGroups.Contains(Key)),
        };
    }

    /// The setting changed: an open page's groups follow it at once, without saving anything.
    private void ApplySetting()
    {
        if (SetProperty(ref _isExpanded, StartsOpen(), nameof(IsExpanded))) OnPropertyChanged(nameof(ShownSummary));
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(ShownSummary));
            if (GroupsOpen.Current != GroupsOpenChoice.Remember) return; // the fixed choices are not the user's own memory
            var settings = AppSettings.Current;
            settings.ExpandedGroups.Remove(Key);
            settings.CollapsedGroups.Remove(Key);
            (value ? settings.ExpandedGroups : settings.CollapsedGroups).Add(Key);
            AppSettings.Current.Save();
        }
    }

    /// True: every card is an on/off switch, so the header gets the master switch (<see cref="GroupMasterSwitch"/>).
    public virtual bool HasMaster => !NoMasterSwitch && _switch is not null;

    /// True: the group never shows a master switch, for a group whose one switch card it would only repeat (Power's Keep awake), and so no "N of M on" count either.
    public bool NoMasterSwitch
    {
        get => _noMasterSwitch;
        init
        {
            _noMasterSwitch = value;
            if (value) _summary = string.Empty;
        }
    }

    private readonly bool _noMasterSwitch;

    /// The master switch's state: true all on, false all off, null some on (the mixed look). Null too when there is no master.
    public virtual bool? MasterState => _switch?.State();

    /// The master switch was clicked: all on becomes all off, anything else becomes all on.
    public virtual void ToggleAll() => _switch?.Toggle();

    private GroupSwitch? _switch;

    /// Gives the group a master switch that is not made of its cards (a screen's power); null takes it away.
    public void UseSwitch(GroupSwitch? groupSwitch)
    {
        _switch = groupSwitch;
        RefreshMaster();
    }

    /// The value behind the switch may have changed: the header shows it again.
    public void RefreshMaster()
    {
        if (_switch?.Count?.Invoke() is var (on, total)) Summary = $"{on} of {total} on";
        OnPropertyChanged(nameof(HasMaster));
        OnPropertyChanged(nameof(MasterState));
    }

    public void Toggle() => IsExpanded = !IsExpanded;
}

/// A group holding editor items (toggles, choices, galleries...) as cards.
/// <remarks>When every item is a <see cref="ToggleItem"/> the group also owns the master switch: its summary reads "N of M on" and follows the switches live.</remarks>
public sealed class ItemGroup : CardGroup, IDisposable
{
    private readonly IReadOnlyList<ToggleItem> _toggles = Array.Empty<ToggleItem>();
    private bool _bulk;

    public IReadOnlyList<object> Items { get; }

    public override bool HasMaster => !NoMasterSwitch && (_toggles.Count > 0 || base.HasMaster);

    public override bool? MasterState
    {
        get
        {
            if (_toggles.Count == 0) return base.MasterState;
            var on = _toggles.Count(t => t.IsOn);
            return on == _toggles.Count ? true : on == 0 ? false : null;
        }
    }

    public ItemGroup(string key, string name, IReadOnlyList<object> items)
        : base(key, name, SettingsCount(items))
    {
        Items = items;
        if (items.Count == 0 || !items.All(i => i is ToggleItem)) return;
        _toggles = items.Cast<ToggleItem>().ToList();
        foreach (var toggle in _toggles) toggle.PropertyChanged += OnToggleChanged;
        Refresh();
    }

    /// A group whose closed header lists what is inside, taken from its cards' names (no hand-written text): "Start with Windows · Start minimised ·
    /// When closing". Cards that come and go later (folder rows) are not in it, so make the group before they are added.
    public static ItemGroup Summarised(string key, string name, IReadOnlyList<object> items) =>
        new(key, name, items) { Summary = string.Join(" · ", items.OfType<IEditorItem>().Select(i => i.Label).Distinct()), SummaryWhenClosed = true, SummaryBesideName = true };

    public override void ToggleAll()
    {
        if (_toggles.Count == 0)
        {
            base.ToggleAll();
            return;
        }
        var target = MasterState != true;
        _bulk = true;
        foreach (var toggle in _toggles) toggle.IsOn = target;
        _bulk = false;
        Refresh();
    }

    private void OnToggleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_bulk && e.PropertyName == nameof(ToggleItem.IsOn)) Refresh();
    }

    /// "N settings" counts the cards that set something; a fact (Now playing, a read-only value) is not a setting, and none says nothing.
    private static string SettingsCount(IReadOnlyList<object> items) => items.Count(i => i is not FactItem) switch
    {
        0 => string.Empty,
        1 => "1 setting",
        var n => $"{n} settings",
    };

    private void Refresh()
    {
        if (!NoMasterSwitch) Summary = $"{_toggles.Count(t => t.IsOn)} of {_toggles.Count} on";
        OnPropertyChanged(nameof(MasterState));
    }

    public void Dispose()
    {
        foreach (var toggle in _toggles) toggle.PropertyChanged -= OnToggleChanged;
    }
}

/// A group that holds groups (the Displays page's Hardware: one group per monitor), and optionally cards of its own above them.
/// The inner groups come and go while the page is open; their keys must be unique like any group's.
public sealed class ParentGroup : CardGroup
{
    /// Cards that belong to the whole group, shown above its inner groups.
    public IReadOnlyList<object> Items { get; }

    public ObservableCollection<ItemGroup> Subgroups { get; } = new();

    public ParentGroup(string key, string name, string summary, IReadOnlyList<object>? items = null)
        : base(key, name, summary) => Items = items ?? Array.Empty<object>();
}
