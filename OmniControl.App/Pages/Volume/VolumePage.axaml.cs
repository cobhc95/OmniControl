using Avalonia.Controls;

namespace OmniControl.App.Pages.Volume;

public partial class VolumePage : UserControl
{
    private VolumePageViewModel? _running;
    private bool _attached;

    public VolumePage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
        // The level meters only run while this page is on screen. The view-model may arrive before or after the page is attached.
        AttachedToVisualTree += (_, _) => { _attached = true; Sync(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; Sync(); };
        DataContextChanged += (_, _) => Sync();
    }

    private void Sync()
    {
        var wanted = _attached ? DataContext as VolumePageViewModel : null;
        if (ReferenceEquals(wanted, _running)) return;
        _running?.StopMeters();
        _running = wanted;
        _running?.StartMeters();
    }
}
