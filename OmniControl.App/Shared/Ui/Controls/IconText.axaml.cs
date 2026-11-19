namespace OmniControl.App.Shared.Ui.Controls;

/// Layout only: a name with an optional library icon in front of it (see IconText.axaml).
public partial class IconText : UserControl
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<IconText, string?>(nameof(Icon));
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<IconText, string?>(nameof(Text));
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<IconText, bool>(nameof(Compact));

    public IconText() => InitializeComponent();

    /// An icon id of the icon library (or a user icon); null or empty: no icon is drawn.
    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// True: the name is the compact card label (wraps to two lines at the card's height); false: a plain one-line name that wraps.
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty)
        {
            Glyph.LibraryId = Icon;
            Glyph.IsVisible = !string.IsNullOrEmpty(Icon);
        }
        else if (change.Property == TextProperty) Words.Text = Text;
        else if (change.Property == CompactProperty)
        {
            Words.Classes.Set("cardlabel", Compact);
            Words.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        }
    }
}
