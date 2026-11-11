using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.ViewModels;

public class NavItem
{
    public NavItem(string name, string icon, ViewModelBase view, string? badge = null, string iconData = "")
    {
        Name = name; Icon = icon; View = view; Badge = badge; IconData = iconData;
    }
    public string Name { get; }
    public string Icon { get; }
    public string IconData { get; }
    public string? Badge { get; }
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
    public ViewModelBase View { get; }
}

public partial class ShellViewModel : ViewModelBase
{
    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty] private NavItem? _selectedTab;
    [ObservableProperty] private ViewModelBase? _currentView;
    [ObservableProperty] private string _searchText = "";
    /// <summary>True while the inline results page (not a dropdown) is shown.</summary>
    [ObservableProperty] private bool _isSearchOpen;

    public ShellViewModel() : this(new MainViewModel())
    {
    }

    public ShellViewModel(IMonitorEngine engine) : this(new MainViewModel(engine))
    {
    }

    public MainViewModel Display { get; }
    public VolumeViewModel Volume { get; }
    public MouseViewModel Mouse { get; }
    public KeyboardViewModel Keyboard { get; }
    public HotkeysViewModel Hotkeys { get; }
    public SmartHomeViewModel Home { get; }
    public SearchViewModel Search { get; }

    private ShellViewModel(MainViewModel display)
    {
        Display = display;
        Volume = new VolumeViewModel(display);
        Mouse = new MouseViewModel(display);
        Keyboard = new KeyboardViewModel(display);
        Hotkeys = new HotkeysViewModel(display);
        Home = new SmartHomeViewModel(display);
        NavItems.Add(new NavItem("Display", "D", display, null,
            "M4,3 H20 A2,2 0 0 1 22,5 V15 A2,2 0 0 1 20,17 H4 A2,2 0 0 1 2,15 V5 A2,2 0 0 1 4,3 Z M8,21 H16 M12,17 V21"));
        NavItems.Add(new NavItem("Volume", "V", Volume, null,
            "M11,5 L6,9 H2 V15 H6 L11,19 Z M15.5,8.5 A5,5 0 0 1 15.5,15.5 M19,5 A10,10 0 0 1 19,19"));
        NavItems.Add(new NavItem("Mouse", "M", Mouse, null,
            "M12,2 A6,6 0 0 1 18,8 V16 A6,6 0 0 1 6,16 V8 A6,6 0 0 1 12,2 Z M12,6 V10"));
        NavItems.Add(new NavItem("Keyboard", "K", Keyboard, null,
            "M4,6 H20 A2,2 0 0 1 22,8 V16 A2,2 0 0 1 20,18 H4 A2,2 0 0 1 2,16 V8 A2,2 0 0 1 4,6 Z M6,10 H6.01 M10,10 H10.01 M14,10 H14.01 M18,10 H18.01 M7,14 H17"));
        NavItems.Add(new NavItem("Hotkeys", "H", Hotkeys, null,
            "M4,6 H20 A2,2 0 0 1 22,8 V16 A2,2 0 0 1 20,18 H4 A2,2 0 0 1 2,16 V8 A2,2 0 0 1 4,6 Z M6,10 H6.01 M10,10 H10.01 M14,10 H14.01 M18,10 H18.01 M7,14 H17"));
        NavItems.Add(new NavItem("Home", "S", Home, null,
            "M3,11 L12,4 L21,11 V20 A1,1 0 0 1 20,21 H15 V15 H9 V21 H4 A1,1 0 0 1 3,20 Z"));
        Search = new SearchViewModel(display, Volume, Mouse, Keyboard, Hotkeys, Home);
        Search.OpenTabRequested += name =>
        {
            var nav = NavItems.FirstOrDefault(n => n.Name == name);
            if (nav != null) SelectedTab = nav;
        };
        SelectedTab = NavItems[0];
        RebuildSearch();
    }

    partial void OnSelectedTabChanged(NavItem? value)
    {
        // Tab click while searching leaves inline mode and lands on the new tab.
        if (!string.IsNullOrWhiteSpace(SearchText))
            SearchText = "";
        CurrentView = value?.View;
    }

    /// <summary>Inline search mode: a non-empty query replaces the tab page with the
    /// live results page (SearchView); empty restores the selected tab. No dropdown.</summary>
    private void RebuildSearch()
    {
        var q = SearchText?.Trim() ?? "";
        if (q.Length == 0)
        {
            IsSearchOpen = false;
            Search.Clear();
            CurrentView = SelectedTab?.View;
            return;
        }
        IsSearchOpen = true;
        Search.Update(q);
        CurrentView = Search;
    }

    partial void OnSearchTextChanged(string value) => RebuildSearch();

    [RelayCommand]
    private void OpenSearch()
    {
        // Focusing the box (Ctrl+K) must not show inline results by itself;
        // only a non-empty typed query switches to the results page.
        RebuildSearch();
    }

    [RelayCommand]
    private void CloseSearch()
    {
        SearchText = "";
        IsSearchOpen = false;
    }

    [RelayCommand]
    private void SelectTab(NavItem? nav)
    {
        if (nav != null) SelectedTab = nav;
    }
}
