using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Controls;

/// What a colour dot carries besides its fill (<see cref="SwatchDot"/>): the small Windows mark (a choice that follows Windows), or the
/// multi-colour hue ring around the user's own colour (a custom choice, so it never looks like a preset).
public enum SwatchMark
{
    None,
    Windows,
    Custom,
}

/// One colour of a colour pop-down (drawn by <see cref="SwatchList"/> as a <see cref="SwatchDot"/>): every colour gallery's choices, the
/// screenshot overlay's drawing colours, the gadget's warn and icon colours, a device's icon colour. A null <see cref="Brush"/> is the
/// "follow the text colour" dot (a dashed ring with an A). <see cref="Tip"/> is its name, shown on hover.
public sealed partial class SwatchItem(IBrush? brush, IRelayCommand pick, string tip = "", SwatchMark mark = SwatchMark.None) : ObservableObject
{
    public IBrush? Brush { get; } = brush;
    public IRelayCommand Pick { get; } = pick;
    public string Tip { get; } = tip;
    public SwatchMark Mark { get; } = mark;

    /// The choice in use: drawn with the accent ring.
    [ObservableProperty] private bool _isSelected;
}

/// THE grid of colour dots (<see cref="SwatchItem"/>s, <see cref="Columns"/> to a line; its dot template is <c>ItemsControl.swatches</c> in
/// Bars.axaml, a class it gives itself because a type selector matches the style key, which is ItemsControl). The arrow keys walk the grid
/// (Left/Right wrap, Down past the last line raises <see cref="PastEnd"/>), Enter or Space picks; <see cref="Picked"/> follows a pick.
/// <see cref="IsMenu"/>: the grid is a pop-down's content, so it takes the keyboard on the chosen dot when it opens and closes the pop-down
/// after a pick. <see cref="IsMouseOnly"/>: its dots take no keyboard focus (the screenshot overlay keeps the keyboard).
public sealed class SwatchList : ItemsControl
{
    /// The dots on one line (owner's design: six to a line).
    public const int Columns = 6;

    public static readonly StyledProperty<bool> IsMenuProperty = AvaloniaProperty.Register<SwatchList, bool>(nameof(IsMenu));
    public static readonly StyledProperty<bool> IsMouseOnlyProperty = AvaloniaProperty.Register<SwatchList, bool>(nameof(IsMouseOnly));

    public SwatchList()
    {
        Classes.Add("swatches");
        AddHandler(Button.ClickEvent, OnPicked);
        AddHandler(KeyDownEvent, OnKeyDown);
    }

    protected override Type StyleKeyOverride => typeof(ItemsControl); // so ItemsControl's theme draws it

    public bool IsMenu { get => GetValue(IsMenuProperty); set => SetValue(IsMenuProperty, value); }
    public bool IsMouseOnly { get => GetValue(IsMouseOnlyProperty); set => SetValue(IsMouseOnlyProperty, value); }

    public event EventHandler<RoutedEventArgs>? Picked;

    /// Down was pressed on the last line: the owner moves the keyboard to what follows the grid (the "Custom colour…" row).
    public event Action? PastEnd;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemCountProperty)
        {
            var columns = Math.Clamp(ItemCount, 1, Columns);
            ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = columns });
        }
        else if (change.Property == IsMouseOnlyProperty) Classes.Set("mouseonly", IsMouseOnly);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsMenu) Dispatcher.UIThread.Post(() => FocusAt(ChosenIndex(), NavigationMethod.Unspecified), DispatcherPriority.Loaded);
    }

    /// Gives the keyboard to the chosen dot (the first when none is chosen).
    public void FocusChosen() => FocusAt(ChosenIndex(), NavigationMethod.Directional);

    private int ChosenIndex()
    {
        var i = 0;
        foreach (var item in Items)
        {
            if (item is SwatchItem { IsSelected: true }) return i;
            i++;
        }
        return 0;
    }

    private void FocusAt(int index, NavigationMethod how)
    {
        if (IsMouseOnly || ContainerFromIndex(index) is not { } container) return;
        container.GetVisualDescendants().OfType<Button>().FirstOrDefault()?.Focus(how);
    }

    private void OnPicked(object? sender, RoutedEventArgs e)
    {
        Picked?.Invoke(this, e);
        // Closed after the dot's command has run (it runs after this Click handler).
        if (IsMenu) Dispatcher.UIThread.Post(() => this.FindLogicalAncestorOfType<Popup>()?.Close());
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Source is not Visual source || IndexOf(source) is not { } at) return;
        var count = ItemCount;
        var columns = Math.Clamp(count, 1, Columns);
        int? to = e.Key switch
        {
            Key.Left => Wrap.Index(at - 1, count),
            Key.Right => Wrap.Index(at + 1, count),
            Key.Up when at >= columns => at - columns,
            Key.Down when at + columns < count => at + columns,
            Key.Down when at / columns < (count - 1) / columns => count - 1, // a shorter last line: its last dot
            _ => null,
        };
        if (to is { } target) FocusAt(target, NavigationMethod.Directional);
        else if (e.Key == Key.Down && PastEnd is { } past) past();
        else if (e.Key != Key.Up) return;
        e.Handled = true; // Up on the first line stays in the grid
    }

    // The index of the dot that holds the source of a key press, or null when it is not one of the dots.
    private int? IndexOf(Visual source)
    {
        for (var visual = source; visual is not null && visual != this; visual = visual.GetVisualParent())
            if (visual is Control control && IndexFromContainer(control) is var index and >= 0) return index;
        return null;
    }
}
