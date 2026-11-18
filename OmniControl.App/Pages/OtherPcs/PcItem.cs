using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Dialogs;
using OmniControl.App.OtherPcs;
using OmniControl.App.Services;
using OmniControl.App.States;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>
/// One paired PC on the Other PCs page: its browser-style tab (name, status dot, route word) and the panel under the tabs (its name, the
/// probe tag, the Connect chip) and its mirrored sections (<see cref="PcMirror"/>, made from what the PC shares, read from the master lists, so they are exactly
/// what hotkeys, rules and search can use too).
/// </summary>
public sealed partial class PcItem : ObservableObject
{
    private readonly OtherPcsService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Func<DialogMessage, Task<string?>> _askText;

    public PcItem(PairedPc pair, OtherPcsService service, Func<DialogMessage, Task<bool>> ask, Func<DialogMessage, Task<string?>> askText, Action<string> report)
    {
        Id = pair.Id;
        _service = service;
        _ask = ask;
        _askText = askText;
        Mirror = new PcMirror(pair.Id, service, ask, report);
        _address = pair.Address;
        Refresh();
    }

    public string Id { get; }

    /// <summary>What the PC is called here: its nickname, or its own name.</summary>
    public string Name => _service.Find(Id)?.DisplayName ?? string.Empty;

    /// <summary>The PC's own name, shown muted beside a nickname in the detail header.</summary>
    public string RealName => _service.Find(Id)?.Name ?? string.Empty;

    public bool HasNickname => _service.Find(Id)?.Nickname.Length > 0;

    /// <summary>The header's one tag: what a probe over the chosen route found (or why not), or when the PC was last seen.</summary>
    [ObservableProperty] private IReadOnlyList<TagItem> _tags = Array.Empty<TagItem>();

    [ObservableProperty] private bool _isOnline;

    /// <summary>The PC is connected but has not yet sent what it shares: each group shows the spinner instead of its cards.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsGroups))] private bool _isLoading;

    /// <summary>The group cards show once what the PC shares has arrived.</summary>
    public bool ShowsGroups => IsOnline && !IsLoading;

    /// <summary>The names and icons of the groups every PC may share, for the loading placeholders (the master list, never a copy).</summary>
    public IReadOnlyList<(string Name, string Icon)> LoadingGroups => CommonGroups.All;

    /// <summary>The PC's shared items as sections with real controls.</summary>
    public PcMirror Mirror { get; }

    /// <summary>The note under the Connect chip while the PC is not connected (why, in one sentence).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    /// <summary>True while the PC is set to Tailscale and Tailscale does not carry its link (the header's tag then says so).</summary>
    [ObservableProperty] private bool _needsTailscale;

    /// <summary>The "Set up Tailscale for this PC" stage shows only while a check fails or the switch over failed; all passing is folded into the header's tag.</summary>
    [ObservableProperty] private bool _showsChecklist;

    /// <summary>The route word in the tab: how the link really runs, or "offline".</summary>
    [ObservableProperty] private string _tabRoute = string.Empty;

    /// <summary>The tab's tooltip: the full name and the tag's detail.</summary>
    [ObservableProperty] private string _tabTip = string.Empty;

    /// <summary>The stage's checklist (the same <see cref="TailscaleChecklist"/> as pairing over the internet, with this PC's extra rows).</summary>
    public ObservableCollection<TailscaleCheckItem> Checks { get; } = new();

    /// <summary>The failed step of the switch to Tailscale while every row passes (the LAN link is kept); empty otherwise.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSwitchProblem))] private string _switchProblem = string.Empty;

    public bool HasSwitchProblem => SwitchProblem.Length > 0;

    /// <summary>The button's words: it is never disabled (a button disabled under the pointer stays pressed), it says a check runs instead.</summary>
    public string CheckText => _service.PairCheckBusy(Id) ? "Checking…" : "Check again";

    [RelayCommand]
    private void CheckAgain() => _service.CheckPairTailscale(Id);

    // ---- Wake (docs/mockups/other-pc-wake.html) ----

    /// <summary>The wake tags in the header line: "Can wake from sleep", "Wake is off on X", "Can't be woken remotely", or "Didn't wake (tried N times)".</summary>
    [ObservableProperty] private IReadOnlyList<TagItem> _wakeTags = Array.Empty<TagItem>();

    /// <summary>The yellow tag's "Turn on" link: opens the set-up window (nothing changes until that PC allows it).</summary>
    [ObservableProperty] private bool _showsTurnOn;

    /// <summary>The red message's "Set up wake…" link.</summary>
    [ObservableProperty] private bool _showsSetUpLink;

    /// <summary>The accent "Wake" pill: the PC is off and can be woken.</summary>
    [ObservableProperty] private bool _showsWake;

    /// <summary>The signal was sent and the PC is awaited: the spinner and "Waking…" replace the pill.</summary>
    [ObservableProperty] private bool _isWaking;

    /// <summary>Asks for the wake set-up window of this PC (the page opens it).</summary>
    public event Action<PcItem>? SetupWakeRequested;

    [RelayCommand]
    private void Wake() => _ = _service.WakeAsync(Id);

    [RelayCommand]
    private void SetUpWake() => SetupWakeRequested?.Invoke(this);

    private void RefreshWake()
    {
        var advice = _service.WakeAdviceOf(Id);
        var status = _service.WakeStatusOf(Id);
        var tags = new List<TagItem>();
        IsWaking = !IsOnline && status.Phase == WakePhase.Waking;
        var failed = !IsOnline && status.Phase == WakePhase.Failed;
        ShowsTurnOn = advice.State == WakeState.SettingOff;
        ShowsWake = !IsWaking && advice.State == WakeState.Wakeable;
        ShowsSetUpLink = failed;
        switch (advice.State)
        {
            case WakeState.CanWake: tags.Add(new TagItem("Can wake from sleep", TagTone.Good, $"It can be woken over the network ({_service.WakeFactsOf(Id)?.Adapter}).")); break;
            case WakeState.SettingOff: tags.Add(new TagItem($"Wake is off on {Name}", TagTone.Caution, "Its network card can wake it, but a setting is off. Turn on opens the set-up; nothing changes there until that PC allows it.")); break;
            case WakeState.Cannot: tags.Add(new TagItem("Can't be woken remotely", TagTone.Neutral, advice.Reason)); break;
        }
        if (failed) tags.Add(new TagItem(OtherPcsService.WakeFailedWords(status), TagTone.Risk, status.Why));
        WakeTags = tags;
    }

    /// <summary>The offline tag: how the PC went away from its notice ("Asleep since 09:12"), or just "Offline" (the last time it was seen in the tip).</summary>
    private TagItem OfflineTag(string? detail)
    {
        var seen = _service.SeenWords(Id);
        var tip = detail is { Length: > 0 } ? $"{detail} ({seen})" : seen;
        return _service.AwayOf(Id) is { } gone ? new TagItem(WakeAdvices.AwayWords(gone.Away, gone.At), TagTone.Neutral, tip) : new TagItem("Offline", TagTone.Neutral, tip);
    }

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
        IsLoading = IsOnline && !_service.HasLiveCatalog(Id);
        OnPropertyChanged(nameof(ShowsGroups));
        // The tag says what a probe over the chosen route really found (an authenticated ping answered by that PC), never what is only chosen.
        var probe = _service.ProbeOf(Id);
        var connected = _service.RouteConnected(Id);
        var word = Route switch { PcRoute.Tailscale => "Tailscale", PcRoute.Address => "Other address", _ => "LAN" };
        NeedsTailscale = _service.NeedsTailscaleSetup(Id);
        TagItem state;
        if (connected) state = new TagItem($"{word} ✓ {probe!.Result.Ms} ms", TagTone.Good, $"Reached over {word}.");
        else if (NeedsTailscale) state = new TagItem(IsOnline ? "Tailscale not working yet · still using LAN" : "Tailscale not working yet · not connected", TagTone.Caution, probe is { Result.Ok: false, Route: PcRoute.Tailscale } ? probe.Result.Reason : null);
        else if (probe is { Result.Ok: false } && IsOnline) state = new TagItem($"{word} ✗ · still using LAN", TagTone.Caution, probe.Result.Reason);
        else if (probe is { Result.Ok: false }) state = OfflineTag($"{word}: {probe.Result.Reason}");
        else state = IsOnline ? new TagItem("checking…", TagTone.Neutral, "Waiting for the first check over this route.") : OfflineTag(null);
        Tags = new[] { state };
        RefreshWake();
        TabRoute = connected ? word : IsOnline ? "LAN" : "offline";
        TabTip = state.Tip is { Length: > 0 } tip ? $"{Name}: {state.Text}. {tip}" : $"{Name}: {state.Text}";
        Checks.Clear();
        if (NeedsTailscale)
            foreach (var check in _service.PairChecklist(Id)) Checks.Add(new TailscaleCheckItem(check, checkId => _service.RunPairTailscaleFix(Id, checkId)));
        SwitchProblem = NeedsTailscale ? _service.PairSwitchProblem(Id) : string.Empty;
        ShowsChecklist = NeedsTailscale && (Checks.Any(c => c.IsFailed) || HasSwitchProblem);
        OnPropertyChanged(nameof(CheckText));
        Note = connected || IsOnline ? string.Empty
            : !_service.HasKey(Id) ? "Its pairing key is missing on this PC. Unpair it and pair again."
            : probe is { Result.Ok: false } ? $"Not connected: {probe.Result.Reason}"
            : "Not connected. It connects by itself when that PC is on and can be reached.";
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(RealName));
        OnPropertyChanged(nameof(HasNickname));
        OnPropertyChanged(nameof(ConnectText));
        OnPropertyChanged(nameof(ShowsAddress));
    }

    /// <summary>Makes the sections again from what the PC shares now.</summary>
    public void Rebuild()
    {
        Mirror.Rebuild();
        IsLoading = IsOnline && !_service.HasLiveCatalog(Id);
    }

    /// <summary>One state's value changed: its card shows it at once.</summary>
    public void Update(StateEntry state) => Mirror.Update(state);

    /// <summary>"Give nickname…": asks for the name (empty removes it); kept on this PC only.</summary>
    public async Task GiveNicknameAsync()
    {
        var pair = _service.Find(Id);
        if (pair is null) return;
        var typed = await _askText(new DialogMessage($"Nickname for {pair.Name}", "Only on this PC. Leave empty to use its own name.", "OK", Input: pair.Nickname));
        if (typed is not null) _service.Rename(Id, typed);
    }

    [RelayCommand]
    private async Task UnpairAsync()
    {
        var name = Name;
        if (await _ask(new DialogMessage($"Unpair {name}?", "Its tab closes and the two PCs stop controlling each other. Pair again to undo.", "Yes, unpair", YesNo: true, Danger: true)))
            _service.Unpair(Id);
    }
}
