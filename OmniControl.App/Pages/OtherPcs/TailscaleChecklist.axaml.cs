using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>Wiring only: the rows (<see cref="TailscaleCheckItem"/>s) are handed to the list inside.</summary>
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
