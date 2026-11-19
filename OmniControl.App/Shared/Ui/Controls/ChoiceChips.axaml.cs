using System.Collections;

namespace OmniControl.App.Shared.Ui.Controls;

/// Layout only: a wrapping row of choice chips over <see cref="ItemsSource"/> (see ChoiceChips.axaml).
public partial class ChoiceChips : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<ChoiceChips, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> ChipTagProperty = AvaloniaProperty.Register<ChoiceChips, string>(nameof(ChipTag), "grouptool");

    public ChoiceChips() => InitializeComponent();

    /// The chips' Tag: "grouptool" on a header line or in a panel, "scope" in a card (the card's parts for Left/Right and Enter).
    public string ChipTag { get => GetValue(ChipTagProperty); set => SetValue(ChipTagProperty, value); }

    /// The choices: each has Name, IsSelected and PickCommand.
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty) Items.ItemsSource = ItemsSource;
    }
}
