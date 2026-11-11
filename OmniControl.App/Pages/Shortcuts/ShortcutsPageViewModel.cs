using System.Collections.ObjectModel;
using OmniControl.App.Hotkeys;

namespace OmniControl.App.Pages.Shortcuts;

/// <summary>Every registered hotkey as an editable, searchable card. Features add theirs via <see cref="HotkeyRegistry"/>.</summary>
public sealed class ShortcutsPageViewModel : PageViewModel
{
    private readonly HotkeyRegistry _registry;

    public override string Title => "Shortcuts";
    public override string Icon => "M4,5 H20 A2,2 0 0 1 22,7 V17 A2,2 0 0 1 20,19 H4 A2,2 0 0 1 2,17 V7 A2,2 0 0 1 4,5 Z M4,7 V17 H20 V7 Z M5,9 H7 V11 H5 Z M9,9 H11 V11 H9 Z M13,9 H15 V11 H13 Z M17,9 H19 V11 H17 Z M7,14 H17 V16 H7 Z";
    public override string Keywords => "keyboard keys hotkeys hotkey keybinding bind shortcut";

    public ObservableCollection<HotkeyAction> Actions => _registry.Actions;

    /// <summary>The actions grouped by feature, in the order features registered them.</summary>
    public ObservableCollection<HotkeyGroup> Groups { get; } = new();

    public override IEnumerable<SearchEntry> SearchEntries =>
        Actions.Select(a => new SearchEntry
        {
            Title = a.Title + " hotkey",
            Category = Title,
            Editor = a,
            Keywords = $"{a.Category} {a.Keywords} keyboard shortcut keybinding",
        });

    public ShortcutsPageViewModel(HotkeyRegistry registry)
    {
        _registry = registry;
        Rebuild();
        registry.Actions.CollectionChanged += (_, _) => Rebuild(); // features register at different times
    }

    private void Rebuild()
    {
        Groups.Clear();
        foreach (var g in Actions.GroupBy(a => a.Category))
            Groups.Add(new HotkeyGroup(g.Key, g.ToList()));
    }

    public override void OnShown()
    {
        foreach (var a in Actions) a.RefreshDevices();
    }
}
