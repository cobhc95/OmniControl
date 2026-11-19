using System.ComponentModel;
using OmniControl.App.Features.OtherPcs;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// One "New PC" tab: the whole pairing flow of one PC still to be paired: the choice On this network / Over the
/// internet, the PCs open to pairing, the typed address, the Tailscale checklist, the code. What is typed is kept with the tab
/// (<see cref="OtherPcsService.UpdateSetupTab"/>); what is real (finding PCs, the exchange, the Tailscale reading and fixes) is the service's.
/// Only one pairing runs at a time: the tab that started it (<see cref="PairingState.TabId"/>) shows its code and message.
public sealed partial class SetupTabItem : ObservableObject, IDisposable
{
    private const string NetworkWords = "On this network", InternetWords = "Over the internet";

    private readonly OtherPcsService _service;
    private readonly TailscaleLink _tailscale;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly bool _loading;
    private ChipItem[]? _modeChips;

    public SetupTabItem(SetupTab tab, OtherPcsService service, TailscaleLink tailscale, Func<DialogMessage, Task<bool>> ask)
    {
        Id = tab.Id;
        _service = service;
        _tailscale = tailscale;
        _ask = ask;
        _loading = true;
        InternetMode = tab.Internet;
        PairAddress = tab.Address;
        TailscaleTarget = tab.Target;
        _loading = false;
        SyncChips();
        service.Pairing.PropertyChanged += OnPairingChanged;
        Refresh();
    }

    public string Id { get; }

    public string Name => "New PC";

    public PairingState Pairing => _service.Pairing;

    public ObservableCollection<NearbyPc> Nearby { get; } = new();

    public ObservableCollection<TailscaleCheckItem> Checks { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsLan), nameof(ShowsInternet))] private bool _internetMode;

    /// The address typed to pair with a PC on ZeroTier or another network.
    [ObservableProperty] private string _pairAddress = string.Empty;

    /// The Tailscale name or address of the PC to pair with (picked from the list or typed).
    [ObservableProperty] private string _tailscaleTarget = string.Empty;

    /// The code typed on this PC (shown on the other).
    [ObservableProperty] private string _typedCode = string.Empty;

    /// The two pills that choose how the PCs find each other.
    public IReadOnlyList<ChipItem> ModeChips => _modeChips ??= new[]
    {
        new ChipItem(NetworkWords, new RelayCommand(() => InternetMode = false), null),
        new ChipItem(InternetWords, new RelayCommand(() => InternetMode = true), null),
    };

    public bool Owns => Pairing.TabId == Id;

    private bool Running => Pairing.IsAsking || Pairing.IsShowing || Pairing.IsWorking;

    /// The choice, the list and the address show unless this tab's pairing is in its code or connecting step.
    public bool ShowsSetup => !(Owns && Running);

    public bool ShowsLan => ShowsSetup && !InternetMode;

    public bool ShowsInternet => ShowsSetup && InternetMode;

    public bool ShowsAsking => Owns && Pairing.IsAsking;

    public bool ShowsCode => Owns && Pairing.IsShowing;

    public bool ShowsMessage => Owns && Pairing.Message.Length > 0;

    /// Pairing is not open for this tab (the time ran out, or it ended): one link opens it again.
    public bool ShowsReopen => Pairing.IsClosed || Pairing.IsEnded || (Pairing.IsOpen && !Owns);

    /// Another New PC tab is pairing now; this one waits.
    public bool OtherBusy => !Owns && Running;

    public bool HasNearby => Nearby.Count > 0;

    public bool NoNearby => Nearby.Count == 0;

    /// Something was typed or picked: closing the tab asks first.
    public bool HasTyped => InternetMode || PairAddress.Length > 0 || TailscaleTarget.Length > 0;

    public string TabTip => "A PC still to be paired. It is kept until you close it.";

    /// The button's words: never disabled (a button disabled under the pointer stays pressed), it says a check runs instead.
    public string CheckText => _tailscale.Busy ? "Checking…" : "Check again";

    partial void OnInternetModeChanged(bool value)
    {
        SyncChips();
        if (_loading) return;
        Keep();
        if (value) _tailscale.Check(TailscaleTarget);
    }

    partial void OnPairAddressChanged(string value)
    {
        OnPropertyChanged(nameof(HasTyped));
        if (!_loading) Keep();
    }

    partial void OnTailscaleTargetChanged(string value)
    {
        OnPropertyChanged(nameof(HasTyped));
        if (!_loading) Keep();
    }

    private void Keep() => _service.UpdateSetupTab(Id, InternetMode, PairAddress, TailscaleTarget);

    private void SyncChips()
    {
        foreach (var chip in ModeChips) chip.IsSelected = chip.Name == (InternetMode ? InternetWords : NetworkWords);
    }

    private void OnPairingChanged(object? sender, PropertyChangedEventArgs e) => RaiseState();

    private void RaiseState()
    {
        foreach (var name in new[] { nameof(Owns), nameof(ShowsSetup), nameof(ShowsLan), nameof(ShowsInternet), nameof(ShowsAsking), nameof(ShowsCode), nameof(ShowsMessage), nameof(ShowsReopen), nameof(OtherBusy), nameof(CheckText) })
            OnPropertyChanged(name);
    }

    /// Reads what the service knows again: the PCs open to pairing, the Tailscale checklist, the pairing state.
    public void Refresh()
    {
        Nearby.Clear();
        foreach (var pc in _service.Nearby) Nearby.Add(pc);
        OnPropertyChanged(nameof(HasNearby));
        OnPropertyChanged(nameof(NoNearby));
        Checks.Clear();
        foreach (var check in _tailscale.Checklist) Checks.Add(new TailscaleCheckItem(check, id => _tailscale.RunFix(id, () => _tailscale.Check(TailscaleTarget))));
        RaiseState();
    }

    /// The tab was opened: this PC is made visible to pairing for this tab, unless another New PC tab is in the middle of a pairing.
    public void Open()
    {
        if (!Running || Owns) _service.OpenPairing(Id);
    }

    // A pairing action started here makes this tab the one that shows its outcome.
    private void Claim()
    {
        if (!Owns) _service.OpenPairing(Id);
    }

    /// The rows of the "online PCs" menu: the tailnet's online Windows PCs (name, then its address), or one muted line saying there are none.
    public IReadOnlyList<MenuRowItem> PeerRows
    {
        get
        {
            var peers = _tailscale.Peers.Where(p => p.Online).ToList();
            if (peers.Count == 0) return new[] { MenuRowItem.Header("No other Windows PC is online in Tailscale.") };
            return peers.Select(p => new MenuRowItem(p.Name, string.Empty, p.Address == TailscaleTarget, p.Address, new RelayCommand(() => PickPeer(p.Address)), Detail: p.Address)).ToList();
        }
    }

    private void PickPeer(string address)
    {
        TailscaleTarget = address;
        _tailscale.Check(address);
    }

    [RelayCommand]
    private void Reopen() => _service.OpenPairing(Id);

    [RelayCommand]
    private void CheckAgain() => _tailscale.Check(TailscaleTarget);

    [RelayCommand]
    private void PairWithNearby(NearbyPc? pc)
    {
        if (pc is null) return;
        Claim();
        _service.PairWith(pc);
    }

    [RelayCommand]
    private void PairWithAddress()
    {
        Claim();
        _service.PairWith(PairAddress);
    }

    [RelayCommand]
    private void PairOverInternet()
    {
        Claim();
        _service.PairOverTailscale(TailscaleTarget);
    }

    [RelayCommand]
    private void SubmitCode()
    {
        _service.SubmitCode(TypedCode);
        TypedCode = string.Empty;
    }

    /// The tab's x (or Esc on it): the tab is taken away; asks first only when something was typed.
    [RelayCommand]
    private async Task CloseAsync()
    {
        if (HasTyped && !await _ask(new DialogMessage("Close this unfinished set-up?", "What you chose and typed here will be lost.", "Yes, close", YesNo: true, Danger: true))) return;
        _service.RemoveSetupTab(Id);
    }

    public void Dispose() => _service.Pairing.PropertyChanged -= OnPairingChanged;
}
