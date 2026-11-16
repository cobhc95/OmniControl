using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Services;

namespace OmniControl.App.Pages;

/// <summary>
/// A collapsible panel of cards. Collapsed by default unless <see cref="ExpandedByDefault"/>; what the user opened or closed is remembered between runs.
/// <see cref="Key"/> must be unique across the program (for example "OSD/Customize").
/// </summary>
public class CardGroup : ObservableObject
{
    private bool _isExpanded;
    private string _summary;
    private ICommand? _restoreCommand;

    public string Key { get; }
    public string Name { get; }

    /// <summary>Facts about the whole group that are not on its cards (what it talks to, its limits), shown as the header's tooltip; null: none.</summary>
    public string? Tip { get; init; }

    /// <summary>True: the summary follows the name on the same line (grey) instead of sitting at the right edge.</summary>
    public bool SummaryBesideName { get; init; }

    /// <summary>Puts the group's settings back to their defaults; set: the header gets the shared "Restore defaults" pill (<see cref="GroupRestorePill"/>), null: none.</summary>
    public Action? Restore { get; init; }

    /// <summary>What the pill's tooltip says it restores.</summary>
    public string? RestoreTip { get; init; }

    public bool HasRestore => Restore is not null;

    public ICommand? RestoreCommand => Restore is null ? null : _restoreCommand ??= new RelayCommand(Restore);

    /// <summary>The short text at the right of the header (a count); a page may update it.</summary>
    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }

    /// <summary>Starts open unless the user closed it themselves (a closed group is remembered in <see cref="AppSettings.CollapsedGroups"/>).</summary>
    public bool ExpandedByDefault
    {
        init { if (value && !AppSettings.Current.CollapsedGroups.Contains(Key)) _isExpanded = true; }
    }

    public CardGroup(string key, string name, string summary)
    {
        Key = key;
        Name = name;
        _summary = summary;
        _isExpanded = AppSettings.Current.ExpandedGroups.Contains(key);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            var settings = AppSettings.Current;
            settings.ExpandedGroups.Remove(Key);
            settings.CollapsedGroups.Remove(Key);
            (value ? settings.ExpandedGroups : settings.CollapsedGroups).Add(Key);
            AppSettings.Current.Save();
        }
    }

    public void Toggle() => IsExpanded = !IsExpanded;
}

/// <summary>A group holding editor items (toggles, choices, galleries...) as cards.</summary>
public sealed class ItemGroup : CardGroup
{
    public IReadOnlyList<object> Items { get; }

    public ItemGroup(string key, string name, IReadOnlyList<object> items)
        : base(key, name, items.Count == 1 ? "1 setting" : $"{items.Count} settings") => Items = items;
}

/// <summary>
/// A group that holds groups (the Displays page's Hardware: one group per monitor), and optionally cards of its own above them.
/// The inner groups come and go while the page is open; their keys must be unique like any group's.
/// </summary>
public sealed class ParentGroup : CardGroup
{
    /// <summary>Cards that belong to the whole group, shown above its inner groups.</summary>
    public IReadOnlyList<object> Items { get; }

    public ObservableCollection<ItemGroup> Subgroups { get; } = new();

    public bool HasItems => Items.Count > 0;

    public ParentGroup(string key, string name, string summary, IReadOnlyList<object>? items = null)
        : base(key, name, summary) => Items = items ?? Array.Empty<object>();
}
