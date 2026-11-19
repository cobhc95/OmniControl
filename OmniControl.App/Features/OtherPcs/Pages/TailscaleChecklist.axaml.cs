using System.Collections;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// Wiring only: the rows (<see cref="TailscaleCheckItem"/>s) are handed to the list inside.
public partial class TailscaleChecklist : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty = AvaloniaProperty.Register<TailscaleChecklist, IEnumerable?>(nameof(Items));

    public TailscaleChecklist() => InitializeComponent();

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
