using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OmniControl.App.Controls;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>Wiring only: a right-click on a PC's tab opens its menu (Give nickname…, Set up wake…, Import settings from… while the PC is online, Unpair) in the shared menu style; a dragged tab is handed to the view-model; Ctrl+Tab switches tabs, Ctrl+T opens a New PC tab, Ctrl+W closes the open one; Esc on an open New PC tab closes it.</summary>
public partial class OtherPcsPage : UserControl
{
    private Flyout? _rowMenu;
    private MenuRowList? _rowRows;

    public OtherPcsPage()
    {
        InitializeComponent();
        PcTabs.TabList.ContextRequested += OnPcContext;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        BrowserTabStrip.Attach(this);
    }

    private void OnPcContext(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: PcItem pc } item) return;
        if (_rowMenu is null)
        {
            _rowRows = new MenuRowList();
            _rowRows.Picked += (_, _) => _rowMenu?.Hide();
            _rowMenu = new ListMenuFlyout { Placement = PlacementMode.Pointer, Content = _rowRows };
        }
        _rowRows!.ItemsSource = pc.MenuRows;
        _rowMenu.ShowAt(item, showAtPointer: true);
        e.Handled = true;
    }

    private void OnTabMoved(object? sender, (int From, int To) move) => (DataContext as OtherPcsPageViewModel)?.Move(move.From, move.To);

    // Esc with the keyboard on the tab itself (not a box in its panel) closes a New PC tab.
    // Ctrl+T and Ctrl+W work while the keyboard is in this page, whatever keys the page's actions are bound to (those are system-wide).
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is OtherPcsPageViewModel page && e.KeyModifiers == KeyModifiers.Control && e.Key is Key.T or Key.W)
        {
            (e.Key == Key.T ? page.NewPcCommand : page.CloseCurrentCommand).Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Escape || e.Source is not ListBoxItem { DataContext: SetupTabItem tab } || DataContext is not OtherPcsPageViewModel vm) return;
        vm.CloseSetupTab(tab);
        e.Handled = true;
    }
}
