using OmniControl.App.Features.OtherPcs;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// This PC's wake status on the page's title line: the same split button as a PC's header (<see cref="WakeSplit"/>),
/// from what this PC read about its own network card; its cogwheel and status half open the one wake set-up window for this PC.
public sealed partial class ThisPcWake : ObservableObject, IDisposable
{
    private readonly OtherPcsService _service;
    private readonly PcWake _wake;

    public ThisPcWake(OtherPcsService service, PcWake wake)
    {
        _service = service;
        _wake = wake;
        service.Changed += Refresh;
        Refresh();
        wake.ReadOwnWake();
    }

    public string Name => OwnWakeTarget.Words;

    [ObservableProperty] private TagItem? _status;

    /// Asks for this PC's wake set-up window (the page opens it).
    public event Action? SetupRequested;

    [RelayCommand]
    private void SetUp() => SetupRequested?.Invoke();

    private void Refresh() => Status = WakeStatusWords.Of(_wake.OwnWakeAdvice(Name), _wake.OwnWakeFacts);

    public void Dispose() => _service.Changed -= Refresh;
}
