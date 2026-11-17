using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.OtherPcs;

// "Over the internet" in the pairing panel (docs/mockups/other-pcs-tailscale.html): the two choice pills, the Tailscale checklist, the list of
// the tailnet's online PCs, the typed name or address. What is read and fixed is OtherPcsService's; this holds what the controls bind to.
public sealed partial class OtherPcsPageViewModel
{
    private const string NetworkWords = "On this network", InternetWords = "Over the internet";

    private ChipItem[]? _modeChips;

    /// <summary>The two pills that choose how the PCs find each other.</summary>
    public IReadOnlyList<ChipItem> ModeChips => _modeChips ??= new[]
    {
        new ChipItem(NetworkWords, new RelayCommand(() => InternetMode = false), null) { IsSelected = true },
        new ChipItem(InternetWords, new RelayCommand(() => InternetMode = true), null),
    };

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsLan), nameof(ShowsInternet))] private bool _internetMode;

    /// <summary>The Tailscale name or address of the PC to pair with (picked from the list or typed).</summary>
    [ObservableProperty] private string _tailscaleTarget = string.Empty;

    public ObservableCollection<TailscaleCheckItem> Checks { get; } = new();

    public bool ShowsLan => Pairing.IsOpen && !InternetMode;

    public bool ShowsInternet => Pairing.IsOpen && InternetMode;

    public bool CanCheckAgain => !_service.TailscaleBusy;

    partial void OnInternetModeChanged(bool value)
    {
        foreach (var chip in ModeChips) chip.IsSelected = chip.Name == (value ? InternetWords : NetworkWords);
        if (value) _service.CheckTailscale(TailscaleTarget);
    }

    // The checklist follows what the service last read.
    private void RefreshChecks()
    {
        Checks.Clear();
        foreach (var check in _service.TailscaleChecklist) Checks.Add(new TailscaleCheckItem(check, id => _service.RunTailscaleFix(id, () => _service.CheckTailscale(TailscaleTarget))));
        OnPropertyChanged(nameof(ShowsLan));
        OnPropertyChanged(nameof(ShowsInternet));
        OnPropertyChanged(nameof(CanCheckAgain));
    }

    /// <summary>The rows of the "online PCs" menu: the tailnet's online Windows PCs (name, then its address), or one muted line saying there are none.</summary>
    public IReadOnlyList<MenuRowItem> PeerRows
    {
        get
        {
            var peers = _service.TailscalePeers.Where(p => p.Online).ToList();
            if (peers.Count == 0) return new[] { MenuRowItem.Header("No other Windows PC is online in Tailscale.") };
            return peers.Select(p => new MenuRowItem(p.Name, string.Empty, p.Address == TailscaleTarget, p.Address, new RelayCommand(() => PickPeer(p.Address)), Detail: p.Address)).ToList();
        }
    }

    private void PickPeer(string address)
    {
        TailscaleTarget = address;
        _service.CheckTailscale(address);
    }

    [RelayCommand]
    private void CheckAgain() => _service.CheckTailscale(TailscaleTarget);

    [RelayCommand]
    private void PairOverInternet() => _service.PairOverTailscale(TailscaleTarget);
}
