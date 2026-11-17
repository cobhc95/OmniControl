using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace OmniControl.App.Controls;

/// <summary>
/// The shared chip and its pop-down menu (layout and wiring only): the chip shows <see cref="Text"/>, a click opens the menu
/// (<see cref="Menu"/>), and closing it puts the keyboard back on the chip. The owner fills the menu when <see cref="MenuOpened"/>
/// is raised (a pop-down's content does not inherit the data context) and tidies up on <see cref="MenuClosed"/>.
/// </summary>
public partial class ChipMenu : UserControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<ChipMenu, string?>(nameof(Text));
    public static readonly StyledProperty<Control?> MenuProperty = AvaloniaProperty.Register<ChipMenu, Control?>(nameof(Menu));
    public static readonly StyledProperty<object?> ChipToolTipProperty = AvaloniaProperty.Register<ChipMenu, object?>(nameof(ChipToolTip));

    public static readonly StyledProperty<object?> ChipTagProperty = AvaloniaProperty.Register<ChipMenu, object?>(nameof(ChipTag), "grouptool");
    public static readonly StyledProperty<HorizontalAlignment> ChipAlignmentProperty =
        AvaloniaProperty.Register<ChipMenu, HorizontalAlignment>(nameof(ChipAlignment), HorizontalAlignment.Right);

    public ChipMenu()
    {
        InitializeComponent();
        (Chip.Tag, Chip.HorizontalAlignment) = (ChipTag, ChipAlignment); // the defaults raise no change
    }

    /// <summary>The chip's Tag: "grouptool" on a group's header line, "scope" in a card (a keyboard stop of the card's parts, <see cref="Pages.RowEditing"/>).</summary>
    public object? ChipTag { get => GetValue(ChipTagProperty); set => SetValue(ChipTagProperty, value); }

    /// <summary>Where the chip sits in its room: at the right on a header line, stretched across a half-width card.</summary>
    public HorizontalAlignment ChipAlignment { get => GetValue(ChipAlignmentProperty); set => SetValue(ChipAlignmentProperty, value); }

    /// <summary>The chip's words.</summary>
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>The pop-down's content.</summary>
    public Control? Menu { get => GetValue(MenuProperty); set => SetValue(MenuProperty, value); }

    /// <summary>The chip's tooltip (also shown while the chip is greyed).</summary>
    public object? ChipToolTip { get => GetValue(ChipToolTipProperty); set => SetValue(ChipToolTipProperty, value); }

    /// <summary>The menu is about to show; the flyout lets the owner close it.</summary>
    public event EventHandler<Flyout>? MenuOpened;

    /// <summary>The menu closed.</summary>
    public event EventHandler? MenuClosed;

    /// <summary>The chip itself (the keyboard stop).</summary>
    public Button ChipButton => Chip;

    private Flyout? Popup => FlyoutBase.GetAttachedFlyout(this) as Flyout;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) Words.Text = Text;
        else if (change.Property == MenuProperty) { if (Popup is { } pop) pop.Content = Menu; }
        else if (change.Property == ChipTagProperty) Chip.Tag = ChipTag;
        else if (change.Property == ChipAlignmentProperty) Chip.HorizontalAlignment = ChipAlignment;
        else if (change.Property == ChipToolTipProperty)
        {
            ToolTip.SetTip(Chip, ChipToolTip);
            ToolTip.SetShowOnDisabled(Chip, true);
        }
    }

    private void OnClick(object? sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        if (Popup is { } pop) MenuOpened?.Invoke(this, pop);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        MenuClosed?.Invoke(this, EventArgs.Empty);
        Chip.Focus(NavigationMethod.Directional);
    }
}
