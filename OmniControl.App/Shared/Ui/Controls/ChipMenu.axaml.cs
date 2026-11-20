using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

namespace OmniControl.App.Shared.Ui.Controls;

/// The shared chip and its pop-down menu (layout and wiring only): the chip shows <see cref="Text"/>, a click opens the menu
/// (<see cref="Menu"/>), and closing it puts the keyboard back on the chip. The owner fills the menu when <see cref="MenuOpened"/>
/// is raised (a pop-down's content does not inherit the data context) and tidies up on <see cref="MenuClosed"/>.
public partial class ChipMenu : UserControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<ChipMenu, string?>(nameof(Text));
    public static readonly StyledProperty<Control?> MenuProperty = AvaloniaProperty.Register<ChipMenu, Control?>(nameof(Menu));
    public static readonly StyledProperty<object?> ChipToolTipProperty = AvaloniaProperty.Register<ChipMenu, object?>(nameof(ChipToolTip));

    public static readonly StyledProperty<object?> ChipTagProperty = AvaloniaProperty.Register<ChipMenu, object?>(nameof(ChipTag), "grouptool");
    public static readonly StyledProperty<HorizontalAlignment> ChipAlignmentProperty =
        AvaloniaProperty.Register<ChipMenu, HorizontalAlignment>(nameof(ChipAlignment), HorizontalAlignment.Right);

    public static readonly StyledProperty<TagTone?> ToneProperty = AvaloniaProperty.Register<ChipMenu, TagTone?>(nameof(Tone));
    public static readonly StyledProperty<SwatchItem?> SwatchProperty = AvaloniaProperty.Register<ChipMenu, SwatchItem?>(nameof(Swatch));

    public ChipMenu()
    {
        InitializeComponent();
        (Chip.Tag, Chip.HorizontalAlignment) = (ChipTag, ChipAlignment); // the defaults raise no change
    }

    /// The chip's Tag: "grouptool" on a group's header line, "scope" in a card (a keyboard stop of the card's parts, <see cref="Pages.RowEditing"/>).
    public object? ChipTag { get => GetValue(ChipTagProperty); set => SetValue(ChipTagProperty, value); }

    /// Where the chip sits in its room: at the right on a header line, stretched across a half-width card.
    public HorizontalAlignment ChipAlignment { get => GetValue(ChipAlignmentProperty); set => SetValue(ChipAlignmentProperty, value); }

    /// A status dot before the words, and the chip's edge for Good (green) and Caution (yellow); null = no dot, the plain chip.
    public TagTone? Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    /// The colour dot before the words (the swatch button of a colour choice); null = none.
    public SwatchItem? Swatch { get => GetValue(SwatchProperty); set => SetValue(SwatchProperty, value); }

    /// The chip's words.
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// The pop-down's content.
    public Control? Menu { get => GetValue(MenuProperty); set => SetValue(MenuProperty, value); }

    /// The chip's tooltip (also shown while the chip is greyed).
    public object? ChipToolTip { get => GetValue(ChipToolTipProperty); set => SetValue(ChipToolTipProperty, value); }

    /// The menu is about to show; the flyout lets the owner close it.
    public event EventHandler<Flyout>? MenuOpened;

    public event EventHandler? MenuClosed;

    /// The chip itself (the keyboard stop).
    public Button ChipButton => Chip;

    private Flyout? Popup => FlyoutBase.GetAttachedFlyout(this) as Flyout;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) Words.Text = Text;
        else if (change.Property == MenuProperty) { if (Popup is { } pop) pop.Content = Menu; }
        else if (change.Property == ToneProperty)
        {
            Dot.Tone = Tone;
            Words.Classes.Set("withdot", Tone is not null);
            Chip.Classes.Set("good", Tone == TagTone.Good);
            Chip.Classes.Set("caution", Tone == TagTone.Caution);
        }
        else if (change.Property == SwatchProperty)
        {
            ColourDot.IsVisible = Swatch is not null;
            (ColourDot.Fill, ColourDot.Mark) = (Swatch?.Brush, Swatch?.Mark ?? SwatchMark.None);
            Words.Classes.Set("withswatch", Swatch is not null);
        }
        else if (change.Property == ChipTagProperty) Chip.Tag = ChipTag;
        else if (change.Property == ChipAlignmentProperty) Chip.HorizontalAlignment = ChipAlignment;
        else if (change.Property == ChipToolTipProperty)
        {
            ToolTip.SetTip(Chip, ChipToolTip);
            ToolTip.SetShowOnDisabled(Chip, true);
        }
    }

    // Shown at the chip itself, not this control, which may be wider than the chip (a header line's chip sits at its right).
    private void OnClick(object? sender, RoutedEventArgs e) => Popup?.ShowAt(Chip);

    // Alt+Down opens the menu, as it opens a drop-down (Enter and Space click the chip).
    private void OnChipKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || e.KeyModifiers != KeyModifiers.Alt) return;
        Popup?.ShowAt(Chip);
        e.Handled = true;
    }

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
