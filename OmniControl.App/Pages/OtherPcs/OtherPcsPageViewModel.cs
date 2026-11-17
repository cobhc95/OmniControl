using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Dialogs;
using OmniControl.App.OtherPcs;
using OmniControl.App.States;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>
/// The Other PCs page (docs/mockups/other-pcs.html): the title line with "+ Pair a PC", the pairing panel while pairing, one pane with the
/// paired PCs on the left (with "+ Pair a PC" under them) and the chosen one on the right (<see cref="PcItem"/>: its basic set as groups).
/// Everything real is <see cref="OtherPcsService"/>'s; this follows its events and holds what the controls bind to.
/// </summary>
public sealed partial class OtherPcsPageViewModel : PageViewModel, IDisposable
{
    private readonly OtherPcsService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action<string> _report;

    public override string Title => OtherPcsService.Category;

    public override string? NavGroup => NavGroups.Devices;

    public override string Icon => "M3,4 H17 V14 H3 Z M5,6 V12 H15 V6 Z M7,16 H13 V18 H7 Z M19,8 H22 V20 H19 Z M20,9.5 V10.5 H21 V9.5 Z";

    public override bool ShowsOwnHeader => true;

    public override string Keywords => "other pcs computers pair pairing code lan network tailscale zeroTier remote share shared states actions run another pc laptop";

    public ObservableCollection<PcItem> Items { get; } = new();

    public ObservableCollection<NearbyPc> Nearby { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelected))] private PcItem? _selected;

    public bool NoPcs => Items.Count == 0;

    public bool HasPcs => Items.Count > 0;

    public bool HasSelected => Selected is not null;

    public PairingState Pairing => _service.Pairing;

    /// <summary>The address typed to pair with a PC on Tailscale, ZeroTier or another network.</summary>
    [ObservableProperty] private string _pairAddress = string.Empty;

    /// <summary>The code typed on this PC (shown on the other).</summary>
    [ObservableProperty] private string _typedCode = string.Empty;

    public bool HasNearby => Nearby.Count > 0;

    public bool NoNearby => Nearby.Count == 0;

    public OtherPcsPageViewModel(OtherPcsService service, Func<DialogMessage, Task<bool>> ask, Action<string> report)
    {
        _service = service;
        _ask = ask;
        _report = report;
        foreach (var pair in service.Pairs) Items.Add(Make(pair));
        Selected = Items.FirstOrDefault();
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(NoPcs));
            OnPropertyChanged(nameof(HasPcs));
        };
        service.Changed += OnServiceChanged;
        service.RemoteChanged += OnRemoteChanged;
        service.RemoteValueChanged += OnRemoteValue;
    }

    private PcItem Make(PairedPc pair) => new(pair, _service, _ask, _report);

    partial void OnSelectedChanged(PcItem? value)
    {
        value?.Rebuild();
    }

    // The list follows the service's pairs (one paired, one unpaired), keeping the rows that stay; online tags and the nearby list refresh.
    private void OnServiceChanged()
    {
        var pairs = _service.Pairs;
        foreach (var gone in Items.Where(i => pairs.All(p => p.Id != i.Id)).ToList()) Items.Remove(gone);
        foreach (var pair in pairs.Where(p => Items.All(i => i.Id != p.Id))) Items.Add(Make(pair));
        foreach (var item in Items) item.Refresh();
        if (Selected is null || !Items.Contains(Selected)) Selected = Items.FirstOrDefault();
        Nearby.Clear();
        foreach (var pc in _service.Nearby) Nearby.Add(pc);
        OnPropertyChanged(nameof(HasNearby));
        OnPropertyChanged(nameof(NoNearby));
        RefreshChecks();
    }

    private void OnRemoteChanged() => Selected?.Rebuild();

    private void OnRemoteValue(StateEntry state) => Selected?.Update(state);

    [RelayCommand]
    private void PairAPc() => _service.OpenPairing();

    [RelayCommand]
    private void ClosePairing() => _service.ClosePairing();

    [RelayCommand]
    private void PairWithNearby(NearbyPc? pc)
    {
        if (pc is not null) _service.PairWith(pc);
    }

    [RelayCommand]
    private void PairWithAddress() => _service.PairWith(PairAddress);

    [RelayCommand]
    private void SubmitCode()
    {
        _service.SubmitCode(TypedCode);
        TypedCode = string.Empty;
    }

    public void Dispose()
    {
        _service.Changed -= OnServiceChanged;
        _service.RemoteChanged -= OnRemoteChanged;
        _service.RemoteValueChanged -= OnRemoteValue;
    }
}
