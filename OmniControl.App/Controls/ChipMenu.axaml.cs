using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

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

    public ChipMenu() => InitializeComponent();

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
