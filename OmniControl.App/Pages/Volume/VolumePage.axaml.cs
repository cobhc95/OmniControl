using Avalonia.Controls;
using Avalonia.Threading;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// Wiring only: the page's keyboard (groups and cards of Advanced, the buttons of Easy, Ctrl+Tab between tabs), the level meters while the page is on screen, and a jump
/// from a Mixer row to its Wiring row (the keyboard moves there and the page scrolls it into view once the tab has been laid out).
/// </summary>
public partial class VolumePage : UserControl
{
    private VolumePageViewModel? _running;
    private bool _attached;

    public VolumePage()
    {
        InitializeComponent();
        GroupedCards.Attach(this, CardsHost);
        GroupedCards.Attach(this, EasyHost);
        PageTabsView.Attach(this);
        // The level meters only run while this page is on screen. The view-model may arrive before or after the page is attached.
        AttachedToVisualTree += (_, _) => { _attached = true; Sync(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; Sync(); };
        DataContextChanged += (_, _) => Sync();
    }

    private void Sync()
    {
        var wanted = _attached ? DataContext as VolumePageViewModel : null;
        if (ReferenceEquals(wanted, _running)) return;
        if (_running is not null)
        {
            _running.StopMeters();
            _running.RevealRequested -= OnReveal;
        }
        _running = wanted;
        if (_running is null) return;
        _running.StartMeters();
        _running.RevealRequested += OnReveal;
    }

    private void OnReveal(object item) =>
        Dispatcher.UIThread.Post(() => GroupedCards.Reveal(CardsHost, item), DispatcherPriority.Loaded);
}
