using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace OmniControl.App.Controls;

/// <summary>Layout only: a wrapping row of choice chips over <see cref="ItemsSource"/> (see ChoiceChips.axaml).</summary>
public partial class ChoiceChips : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty = AvaloniaProperty.Register<ChoiceChips, IEnumerable?>(nameof(ItemsSource));

    public ChoiceChips() => InitializeComponent();

    /// <summary>The choices: each has Name, IsSelected and PickCommand.</summary>
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty) Items.ItemsSource = ItemsSource;
    }
}
