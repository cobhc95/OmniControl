using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// Wiring only. A picture gallery: the arrows, a click on a picture and the wheel step or pick. A colour gallery: each swatch button's
/// pop-down is filled with the dots when it opens and closes after a pick; its "Custom colour…" row (or Down past the dots, then Enter)
/// opens the item's editor at the button; the wheel over the button steps the choice as it steps a drop-down.
public partial class GalleryRow : UserControl
{
    private GalleryItem? _hooked;
    private Flyout? _open;

    public GalleryRow()
    {
        InitializeComponent();
        foreach (var chip in new[] { FullChip, HalfChip }) chip.Menu = DotsMenu(chip);
        DataContextChanged += (_, _) =>
        {
            FitHalf();
            Hook(VisualRoot is not null);
        };
    }

    /// The pop-down of a swatch button: the grid of dots, then a thin line and the muted editor row (shown only for a gallery with an editor).
    private StackPanel DotsMenu(ChipMenu chip)
    {
        var list = new SwatchList { IsMenu = true, HorizontalAlignment = HorizontalAlignment.Center };
        var rule = new Border();
        rule.Classes.Add("hrule");
        var words = new TextBlock();
        var row = new Button { Content = words };
        row.Classes.AddRange(new[] { "menurow", "muted" });
        row.Click += (_, _) => OpenEditorFromMenu();
        row.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Up || e.KeyModifiers != KeyModifiers.None) return;
            list.FocusChosen();
            e.Handled = true;
        };
        list.PastEnd += () => { if (row.IsVisible) row.Focus(NavigationMethod.Directional); };
        chip.MenuOpened += (_, flyout) =>
        {
            _open = flyout;
            if (DataContext is not GalleryItem item) return;
            list.ItemsSource = item.DotItems();
            words.Text = item.EditorText;
            rule.IsVisible = row.IsVisible = item.Editor is not null && item.EditorText is not null;
        };
        chip.MenuClosed += (_, _) => _open = null;
        var menu = new StackPanel { Children = { list, rule, row } };
        menu.Bind(StackPanel.SpacingProperty, menu.GetResourceObservable("Gap"));
        return menu;
    }

    // The menu closes first; the editor then opens at the same button.
    private void OpenEditorFromMenu()
    {
        _open?.Hide();
        Dispatcher.UIThread.Post(() => (DataContext as GalleryItem)?.OpenEditor());
    }

    /// A half-width picture gallery has no room for the page-wide label column: its label takes only its own width (it leaves the shared
    /// column group) and the gallery sits at the right end (the half style in Theme.axaml).
    private void FitHalf()
    {
        if (DataContext is not GalleryItem { Half: true }) return;
        var label = Layout.ColumnDefinitions[0];
        label.SharedSizeGroup = null;
        label.MinWidth = 0;
        LabelText.Margin = new Thickness(0, 0, 8, 0);
    }

    // The item's editor request is heard only while this row is on screen and shows that item.
    private void Hook(bool onScreen)
    {
        var item = onScreen ? DataContext as GalleryItem : null;
        if (item == _hooked) return;
        if (_hooked is not null) _hooked.EditorRequested -= OpenEditor;
        _hooked = item;
        if (_hooked is not null) _hooked.EditorRequested += OpenEditor;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Hook(onScreen: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Hook(onScreen: false);
    }

    /// The option's editor in the master card pop-down (ListMenuFlyout, optionmenu) under the gallery, made fresh each time.
    private void OpenEditor()
    {
        if (_hooked?.Editor is not { } make) return;
        var flyout = new ListMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.FlyoutPresenterClasses.Add("optionmenu");
        flyout.Content = make(flyout.Hide);
        flyout.ShowAt(Opener());
    }

    // Where the editor opens: the swatch button shown, else the picture gallery.
    private Control Opener() => _hooked switch
    {
        { FullDots: true } => FullChip.ChipButton,
        { HalfDots: true } => HalfChip.ChipButton,
        _ => Gallery,
    };

    private void OnPrevious(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(-1);
    private void OnNext(object? sender, RoutedEventArgs e) => (DataContext as GalleryItem)?.Step(1);

    /// The wheel over a picture gallery steps the choice (up = next), like the arrow buttons.
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not GalleryItem item || !ValueWheel.IsOverSelector(e.Source as Visual)) return;
        item.Step(ValueWheel.Direction(e));
        e.Handled = true;
    }

    /// The wheel over a closed swatch button steps the choice as it steps a closed drop-down (up = the one before).
    private void OnChipWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not GalleryItem item || _open is not null) return;
        item.Step(ValueWheel.ListDirection(e));
        e.Handled = true;
    }

    /// A click on one of a picture gallery's pictures picks it; a click anywhere else on the card just focuses it.
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control card || DataContext is not GalleryItem item) return;
        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
        for (var visual = e.Source as Visual; visual is not null && visual != card; visual = visual.GetVisualParent())
        {
            if (visual is not Border { Tag: int index } picture || !picture.Classes.Contains(GalleryStrip.Clickable)) continue;
            card.Focus();
            item.Index = index;
            return;
        }
    }
}
