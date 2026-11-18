using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OmniControl.App.Services;

namespace OmniControl.App.Controls;

/// <summary>
/// Wiring only for THE browser-style tab strip: the items and their content template go to the list inside, the open tab follows
/// <see cref="SelectedItem"/> both ways, a drag (<see cref="PanelDragReorder"/>) raises <see cref="Moved"/> once on drop (Esc puts it back), and
/// <see cref="Attach"/> gives a page Ctrl+Tab / Ctrl+Shift+Tab like <see cref="Pages.PageTabsView"/>. The whole strip is one Tab stop; Left/Right walk the tabs.
/// </summary>
public partial class BrowserTabStrip : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<BrowserTabStrip, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<object?> SelectedItemProperty = AvaloniaProperty.Register<BrowserTabStrip, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<IDataTemplate?> TabContentProperty = AvaloniaProperty.Register<BrowserTabStrip, IDataTemplate?>(nameof(TabContent));
    public static readonly StyledProperty<ICommand?> AddCommandProperty = AvaloniaProperty.Register<BrowserTabStrip, ICommand?>(nameof(AddCommand));
    public static readonly StyledProperty<string?> AddToolTipProperty = AvaloniaProperty.Register<BrowserTabStrip, string?>(nameof(AddToolTip));

    private readonly PanelDragReorder _drag;

    public BrowserTabStrip()
    {
        InitializeComponent();
        _drag = new PanelDragReorder(Tabs, vertical: false, () => true, _ => true, (from, to) => Moved?.Invoke(this, (from, to)));
        Tabs.SelectionChanged += (_, _) => SetCurrentValue(SelectedItemProperty, Tabs.SelectedItem);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _drag.Cancel()) e.Handled = true;
            else if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None && e.Source is ListBoxItem)
            {
                Step(e.Key == Key.Right ? 1 : -1);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }

    /// <summary>The tab at the first index was dropped at the second (the item's index before and after).</summary>
    public event EventHandler<(int From, int To)>? Moved;

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }

    /// <summary>How one tab looks inside (the master tab chrome is the style's).</summary>
    public IDataTemplate? TabContent { get => GetValue(TabContentProperty); set => SetValue(TabContentProperty, value); }

    /// <summary>What the "+" at the strip's end does; null hides it.</summary>
    public ICommand? AddCommand { get => GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }

    public string? AddToolTip { get => GetValue(AddToolTipProperty); set => SetValue(AddToolTipProperty, value); }

    /// <summary>The tab list, for a page that opens a right-click menu on a tab.</summary>
    public ListBox TabList => Tabs;

    /// <summary>Ctrl+Tab and Ctrl+Shift+Tab anywhere on <paramref name="page"/> open the next or previous tab of its visible strip.</summary>
    public static void Attach(Control page) => page.AddHandler(KeyDownEvent, (_, e) =>
    {
        if (e.Key != Key.Tab || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.Control) return;
        if (page.GetVisualDescendants().OfType<BrowserTabStrip>().FirstOrDefault(v => v.IsEffectivelyVisible) is not { } strip) return;
        strip.Step(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
        e.Handled = true;
    }, RoutingStrategies.Tunnel);

    /// <summary>Opens the next or previous tab (wrapping) and gives it the keyboard once it is laid out (only the open tab takes focus).</summary>
    private void Step(int direction)
    {
        if (Tabs.ItemCount < 2) return;
        Tabs.SelectedIndex = Wrap.Index(Tabs.SelectedIndex + direction, Tabs.ItemCount);
        Dispatcher.UIThread.Post(() => Tabs.ContainerFromIndex(Tabs.SelectedIndex)?.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The list may take the width it is given less the "+"; its tabs shrink to that (<see cref="TabFitPanel"/>) only when they would not fit, before it scrolls.
    /// The width comes from this measure, never from a size read after layout (that was still the old, narrow one on the first pass and shrank the names).
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        AddButton.Measure(Size.Infinity);
        var room = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - (AddButton.IsVisible ? AddButton.DesiredSize.Width : 0));
        if (Tabs.MaxWidth != room)
        {
            Tabs.MaxWidth = room;
            Tabs.ItemsPanelRoot?.InvalidateMeasure(); // the panel reads the list's MaxWidth, not its constraint
        }
        return base.MeasureOverride(availableSize);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty) Tabs.ItemsSource = ItemsSource;
        else if (change.Property == TabContentProperty) Tabs.ItemTemplate = TabContent;
        else if (change.Property == SelectedItemProperty && !ReferenceEquals(Tabs.SelectedItem, SelectedItem)) Tabs.SelectedItem = SelectedItem;
        else if (change.Property == AddCommandProperty) (AddButton.Command, AddButton.IsVisible) = (AddCommand, AddCommand is not null);
        else if (change.Property == AddToolTipProperty) ToolTip.SetTip(AddButton, AddToolTip);
    }
}
