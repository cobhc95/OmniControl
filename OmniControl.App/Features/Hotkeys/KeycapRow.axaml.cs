namespace OmniControl.App.Features.Hotkeys;

/// Draws the <see cref="KeycapPart"/>s it is given (see <see cref="Keycaps.From"/>).
public partial class KeycapRow : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<KeycapPart>?> PartsProperty =
        AvaloniaProperty.Register<KeycapRow, IReadOnlyList<KeycapPart>?>(nameof(Parts));

    public KeycapRow() => InitializeComponent();

    public IReadOnlyList<KeycapPart>? Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PartsProperty) Host.ItemsSource = Parts;
    }
}
