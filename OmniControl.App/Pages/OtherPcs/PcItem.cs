using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Dialogs;
using OmniControl.App.OtherPcs;
using OmniControl.App.Services;
using OmniControl.App.States;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>
/// One paired PC on the Other PCs page: a row of the list (name, online or offline tag) and the detail on the right (its name, the Connect
/// chip) and its mirrored sections (<see cref="PcMirror"/>, made from what the PC shares, read from the master lists, so they are exactly
/// what hotkeys, rules and search can use too).
/// </summary>
public sealed partial class PcItem : ObservableObject
{
    private readonly OtherPcsService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;

    public PcItem(PairedPc pair, OtherPcsService service, Func<DialogMessage, Task<bool>> ask, Action<string> report)
    {
        Id = pair.Id;
        _service = service;
        _ask = ask;
        Mirror = new PcMirror(pair.Id, service, ask, report);
        _address = pair.Address;
        Refresh();
    }

    public string Id { get; }

    public string Name => _service.Find(Id)?.Name ?? string.Empty;

    /// <summary>The list row's tag and the detail's: online (and how) or offline.</summary>
    [ObservableProperty] private IReadOnlyList<TagItem> _tags = Array.Empty<TagItem>();

    [ObservableProperty] private bool _isOnline;

    /// <summary>The PC's shared items as sections with real controls.</summary>
    public PcMirror Mirror { get; }

    /// <summary>The note under the Connect chip while the PC is not connected (why, in one sentence).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    /// <summary>True while the PC is set to Tailscale and Tailscale does not carry its link: the "Set up Tailscale for this PC" stage shows, with the tag under the title line.</summary>
    [ObservableProperty] private bool _needsTailscale;

    /// <summary>The caution tag under the title line while <see cref="NeedsTailscale"/>: honest about what is used meanwhile.</summary>
    [ObservableProperty] private IReadOnlyList<TagItem> _setupTags = Array.Empty<TagItem>();

    /// <summary>The stage's checklist (the same <see cref="TailscaleChecklist"/> as pairing over the internet, with this PC's extra rows).</summary>
    public ObservableCollection<TailscaleCheckItem> Checks { get; } = new();

    /// <summary>The failed step of the switch to Tailscale while every row passes (the LAN link is kept); empty otherwise.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSwitchProblem))] private string _switchProblem = string.Empty;

    public bool HasSwitchProblem => SwitchProblem.Length > 0;

    public bool CanCheckAgain => !_service.PairCheckBusy(Id);

    [RelayCommand]
    private void CheckAgain() => _service.CheckPairTailscale(Id);

    // ---- Connect ----

    private PcRoute Route => _service.Find(Id)?.Route ?? PcRoute.Lan;

    public string ConnectText => Route switch { PcRoute.Lan => "Connect: LAN", PcRoute.Tailscale => "Connect: Tailscale", _ => "Connect: other address" };

    public bool ShowsAddress => Route == PcRoute.Address;

    /// <summary>The address typed for a PC on ZeroTier or another network; applied when the box is left.</summary>
    [ObservableProperty] private string _address = string.Empty;

    public IReadOnlyList<MenuRowItem> ConnectRows => new[]
    {
        new MenuRowItem("LAN", string.Empty, Route == PcRoute.Lan, "Found on the local network by itself.", new RelayCommand(() => SetRoute(PcRoute.Lan))),
        new MenuRowItem("Tailscale", string.Empty, Route == PcRoute.Tailscale, "Reached over Tailscale; this page checks what is still needed.", new RelayCommand(() => SetRoute(PcRoute.Tailscale))),
        new MenuRowItem("Other address", string.Empty, Route == PcRoute.Address, "Reached at an address on another network such as ZeroTier; type it below.", new RelayCommand(() => SetRoute(PcRoute.Address))),
    };

    private void SetRoute(PcRoute route)
    {
        _service.SetRoute(Id, route, Address);
        OnPropertyChanged(nameof(ConnectText));
        OnPropertyChanged(nameof(ShowsAddress));
    }

    /// <summary>The address box was left or confirmed: the PC is reached there from now on.</summary>
    public void ApplyAddress()
    {
        if (Route == PcRoute.Address) _service.SetRoute(Id, Route, Address);
    }

    // ---- What the service knows ----

    /// <summary>Reads the online state, the tags and the note again.</summary>
    public void Refresh()
    {
        IsOnline = _service.IsOnline(Id);
        var viaTailscale = _service.IsViaTailscale(Id);
        // The tags say how the PC is really reached, never what is only chosen.
        var how = viaTailscale ? "online" : Route == PcRoute.Address ? "online · other address" : "online · LAN";
        var state = IsOnline ? new TagItem(how, TagTone.Good) : new TagItem(_service.SeenWords(Id));
        Tags = IsOnline && viaTailscale ? new[] { state, new TagItem("via Tailscale") } : new[] { state };
        NeedsTailscale = _service.NeedsTailscaleSetup(Id);
        SetupTags = NeedsTailscale ? new[] { new TagItem(IsOnline ? "Tailscale not set up yet · still using LAN" : "Tailscale not set up yet · not connected", TagTone.Caution) } : Array.Empty<TagItem>();
        Checks.Clear();
        if (NeedsTailscale)
            foreach (var check in _service.PairChecklist(Id)) Checks.Add(new TailscaleCheckItem(check, checkId => _service.RunPairTailscaleFix(Id, checkId)));
        SwitchProblem = NeedsTailscale ? _service.PairSwitchProblem(Id) : string.Empty;
        OnPropertyChanged(nameof(CanCheckAgain));
        Note = IsOnline ? string.Empty
            : !_service.HasKey(Id) ? "Its pairing key is missing on this PC. Unpair it and pair again."
            : "Not connected. It connects by itself when that PC is on and can be reached.";
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ConnectText));
        OnPropertyChanged(nameof(ShowsAddress));
    }

    /// <summary>Makes the sections again from what the PC shares now.</summary>
    public void Rebuild() => Mirror.Rebuild();

    /// <summary>One state's value changed: its card shows it at once.</summary>
    public void Update(StateEntry state) => Mirror.Update(state);

    [RelayCommand]
    private async Task UnpairAsync()
    {
        var name = Name;
        if (await _ask(new DialogMessage($"Unpair {name}?", $"\"{name}\" will no longer see or run anything here. Its entries leave the lists. The other PC keeps its side until it is unpaired there too.", "Unpair", RememberKey: "unpair.pc", RememberName: "Unpair a PC")))
            _service.Unpair(Id);
    }
}
