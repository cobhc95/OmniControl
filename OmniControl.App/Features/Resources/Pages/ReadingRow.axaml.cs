namespace OmniControl.App.Features.Resources.Pages;

/// Wiring only: the card's chip menus (network adapter, temperature sensor), each fed by one of the card's items (<see cref="MenuChoiceLink"/>).
public partial class ReadingRow : UserControl
{
    private readonly MenuChoiceLink _adapter, _sensor;

    public ReadingRow()
    {
        InitializeComponent();
        _adapter = new MenuChoiceLink(AdapterChip, this, NetworkAdapterMenu.Title);
        _sensor = new MenuChoiceLink(SensorChip, this, SensorMenu.Title);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        var card = DataContext as ReadingCard;
        _adapter.Follow(card?.Adapter);
        _sensor.Follow(card?.Sensor);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _adapter.Attach();
        _sensor.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _adapter.Detach();
        _sensor.Detach();
    }
}
