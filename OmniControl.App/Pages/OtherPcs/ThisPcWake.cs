using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.OtherPcs;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>
/// This PC's wake status on the page's title line (docs/mockups/this-pc-wake.html, pc-header-tidy.html): the same split button as a PC's header (<see cref="WakeSplit"/>),
/// from what this PC read about its own network card; its cogwheel and status half open the one wake set-up window for this PC.
/// </summary>
public sealed partial class ThisPcWake : ObservableObject, IDisposable
{
    private readonly OtherPcsService _service;

    public ThisPcWake(OtherPcsService service)
    {
        _service = service;
        service.Changed += Refresh;
        Refresh();
        service.ReadOwnWake();
    }

    public string Name => OwnWakeTarget.Words;

    [ObservableProperty] private TagItem? _status;

    /// <summary>Asks for this PC's wake set-up window (the page opens it).</summary>
    public event Action? SetupRequested;

    [RelayCommand]
    private void SetUp() => SetupRequested?.Invoke();

    private void Refresh() => Status = WakeStatusWords.Of(_service.OwnWakeAdvice(Name), _service.OwnWakeFacts);

    public void Dispose() => _service.Changed -= Refresh;
}
