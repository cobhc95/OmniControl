using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Services;

namespace OmniControl.App.Pages;

/// <summary>
/// A collapsible panel of cards. Collapsed by default; the groups the user expanded are remembered between runs.
/// <see cref="Key"/> must be unique across the program (for example "OSD/Customize").
/// </summary>
public class CardGroup : ObservableObject
{
    private bool _isExpanded;
    private string _summary;

    public string Key { get; }
    public string Name { get; }

    /// <summary>The short text at the right of the header (a count); a page may update it.</summary>
    public string Summary { get => _summary; set => SetProperty(ref _summary, value); }

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
            var expanded = AppSettings.Current.ExpandedGroups;
            expanded.Remove(Key);
            if (value) expanded.Add(Key);
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
