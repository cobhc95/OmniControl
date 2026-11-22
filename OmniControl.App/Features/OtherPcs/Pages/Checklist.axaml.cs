using System.Collections;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// Wiring only: the rows (<see cref="CheckItem"/>s) are handed to the list inside.
public partial class Checklist : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty = AvaloniaProperty.Register<Checklist, IEnumerable?>(nameof(Items));

    public Checklist() => InitializeComponent();

    public IEnumerable? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty) List.ItemsSource = Items;
    }
}
