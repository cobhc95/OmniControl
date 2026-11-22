using OmniControl.App.Features.OtherPcs;
using OmniControl.App.Features.States;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// One paired PC on the Other PCs page: its browser-style tab (name, status dot, route word) and the panel under the tabs (its name, the
/// probe tag, the Connect chip) and its mirrored sections (<see cref="PcMirror"/>, made from what the PC shares, read from the master lists, so they are exactly
/// what hotkeys, rules and search can use too).
public sealed partial class PcItem : ObservableObject
{
    private readonly OtherPcsService _service;
    private readonly PcWake _wake;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Func<DialogMessage, Task<string?>> _askText;

    public PcItem(PairedPc pair, OtherPcsService service, PcWake wake, Func<DialogMessage, Task<bool>> ask, Func<DialogMessage, Task<string?>> askText, Action<string> report)
    {
        Id = pair.Id;
        _service = service;
        _wake = wake;
        _ask = ask;
        _askText = askText;
        Mirror = new PcMirror(pair.Id, service, ask, report);
        Reach = new ReachHelp(RunReachFix);
        _address = pair.Address;
        Refresh();
    }

    public string Id { get; }

    /// What the PC is called here: its nickname, or its own name.
    public string Name => _service.Find(Id)?.DisplayName ?? string.Empty;

    /// The PC's own name, shown muted beside a nickname in the detail header.
    public string RealName => _service.Find(Id)?.Name ?? string.Empty;

    public bool HasNickname => _service.Find(Id)?.Nickname.Length > 0;

    /// The connection chip's words and tone: what a probe over the chosen route found (or why not), or how the PC went away.
    [ObservableProperty] private TagItem _connection = new("Checking…");

    [ObservableProperty] private bool _isOnline;

    /// The PC is connected but has not yet sent what it shares: each group shows the spinner instead of its cards.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsGroups))] private bool _isLoading;

    /// The group cards show once what the PC shares has arrived.
    public bool ShowsGroups => IsOnline && !IsLoading;

    /// The names and icons of the groups every PC may share, for the loading placeholders (the master list, never a copy).
    public IReadOnlyList<CommonGroup> LoadingGroups => CommonGroups.All;

    /// The PC's shared items as sections with real controls.
    public PcMirror Mirror { get; }

    /// The note under the header while the PC is not connected (why, in one sentence).
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNote))] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    /// True while the PC is set to Tailscale and Tailscale does not carry its link (the header's tag then says so).
    [ObservableProperty] private bool _needsTailscale;

    /// The "Set up Tailscale for this PC" stage shows only while a check fails or the switch over failed; all passing is folded into the header's tag.
    [ObservableProperty] private bool _showsChecklist;

    /// The route word in the tab: how the link really runs, or "offline".
    [ObservableProperty] private string _tabRoute = string.Empty;

    /// The tab's tooltip: the full name and the tag's detail.
    [ObservableProperty] private string _tabTip = string.Empty;

    /// The stage's checklist (the same <see cref="Checklist"/> as pairing over the internet, with this PC's extra rows).
    public ObservableCollection<CheckItem> Checks { get; } = new();

    /// The failed step of the switch to Tailscale while every row passes (the LAN link is kept); empty otherwise.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSwitchProblem))] private string _switchProblem = string.Empty;

    public bool HasSwitchProblem => SwitchProblem.Length > 0;

    /// The button's words: it is never disabled (a button disabled under the pointer stays pressed), it says a check runs instead.
    public string CheckText => _service.PairCheckBusy(Id) ? "Checking…" : "Check again";

    [RelayCommand]
    private void CheckAgain() => _service.CheckPairTailscale(Id);

    // ---- Can't reach it on this network? ----

    /// "Can't reach it on this network? Check:" shows under the header while the PC is offline over the local network (a PC set to Tailscale has its own stage).
    [ObservableProperty] private bool _showsReach;

    public ReachHelp Reach { get; }

    // Its Tailscale row chooses Tailscale for this PC, which shows the Tailscale stage; the others open the Windows page that holds the fix.
    private void RunReachFix(string checkId)
    {
        if (checkId == NetworkChecks.Tailscale) SetRoute(PcRoute.Tailscale);
        else _service.RunReachFix(checkId);
    }

    private void RefreshReach()
    {
        ShowsReach = !IsOnline && Route == PcRoute.Lan;
        if (!ShowsReach) return;
        _service.WatchFirewall();
        Reach.Show(NetworkChecks.ForPairedPc(_service.NetworkIsPrivate, _service.FirewallAllowed));
    }

    // ---- Wake ----

    /// The wake status in the left half of the split Wake button: "Wake ready", "Wake not set up", "Wake unconfirmed" or "Can't wake" (the reason in its tooltip).
    [ObservableProperty] private TagItem? _wakeStatus;

    /// The red "Didn't wake" note beside the buttons after a wake that was not answered (its reason in the tooltip); empty otherwise.
    [ObservableProperty] private IReadOnlyList<TagItem> _wakeFailed = Array.Empty<TagItem>();

    /// The left half of the split button is the accent "Wake" action: the PC is off and can be woken.
    [ObservableProperty] private bool _showsWake;

    /// The Wake action is offered but the PC's wake could not be confirmed: it carries a caution mark.
    [ObservableProperty] private bool _wakeUnconfirmed;

    /// The Wake action's tooltip.
    [ObservableProperty] private string _wakeTip = string.Empty;

    /// The signal was sent and the PC is awaited: the left half says "Waking…" with the shared spinner.
    [ObservableProperty] private bool _isWaking;

    /// Asks for the wake set-up window of this PC (the page opens it).
    public event Action<PcItem>? SetupWakeRequested;

    [RelayCommand]
    private void Wake() => _ = Guard.RunAsync("Other PCs: wake", () => _wake.WakeAsync(Id, _wake.GrantWake(Id)));

    [RelayCommand]
    private void SetUpWake() => SetupWakeRequested?.Invoke(this);

    /// Asks for the "Import settings from this PC" window (the page opens it; only while the PC is online).
    public event Action<PcItem>? ImportRequested;

    [RelayCommand]
    private void ImportSettings() => ImportRequested?.Invoke(this);

    private void RefreshWake()
    {
        var advice = _wake.WakeAdviceOf(Id);
        var status = _wake.WakeStatusOf(Id);
        IsWaking = !IsOnline && status.Phase == WakePhase.Waking;
        var failed = !IsOnline && status.Phase == WakePhase.Failed;
        WakeUnconfirmed = advice.State == WakeState.Unconfirmed;
        ShowsWake = !IsWaking && !IsOnline && advice.State is WakeState.Wakeable or WakeState.Unconfirmed;
        WakeTip = WakeUnconfirmed ? $"Not guaranteed: {Name} couldn't confirm its card can wake it." : "Send the wake signal to this PC over the network.";
        WakeStatus = WakeStatusWords.Of(advice, _wake.WakeFactsOf(Id));
        WakeFailed = failed ? new[] { new TagItem(PcWake.FailedWords(status), TagTone.Risk, status.Why) } : Array.Empty<TagItem>();
    }

    /// The offline status: how the PC went away from its notice ("Asleep since 09:12"), or just "Offline" (the last time it was seen in the tip).
    private TagItem OfflineTag(string? detail)
    {
        var seen = _service.SeenWords(Id);
        var tip = detail is { Length: > 0 } ? $"{detail} ({seen})" : seen;
        return _wake.AwayOf(Id) is { } gone ? new TagItem(WakeAdvices.AwayWords(gone.Away, gone.At), TagTone.Neutral, tip) : new TagItem("Offline", TagTone.Neutral, tip);
    }

    // ---- Power countdown ----

    /// The power tag on the tab and the header, from any section: "Sleep in 0:25" while that PC counts down an action asked from here, "Going to sleep…"
    /// once it ran out, "Asleep" or "Off" once it went away; null otherwise.
    [ObservableProperty] private TagItem? _powerTag;

    private DispatcherTimer? _tick;

    /// Shows that PC's countdown again (it changed, or a second passed); the tick runs only while a countdown does.
    public void RefreshCountdown()
    {
        var now = _service.CountdownOf(Id);
        var left = now is null ? TimeSpan.Zero : now.Ends - DateTime.UtcNow;
        var counting = now is not null && IsOnline && left > TimeSpan.Zero;
        PowerTag = now is null ? null
            : !IsOnline ? new TagItem(WakeAdvices.AwayWord(_wake.AwayOf(Id)?.Away), TagTone.Neutral)
            : counting ? new TagItem($"{now.Action.Action.Title} in {PcCountdown.Clock(left)}", TagTone.Selected)
            : new TagItem(now.Words.Going, TagTone.Selected);
        Mirror.ShowCountdown(IsOnline ? now : null, left);
        if (!counting) _tick?.Stop();
        else
        {
            _tick ??= new DispatcherTimer(Timing.CountdownTick, DispatcherPriority.Background, (_, _) => Guard.Run("Other PCs: countdown", RefreshCountdown));
            _tick.Start();
        }
    }

    // ---- Connect ----

    private PcRoute Route => _service.Find(Id)?.Route ?? PcRoute.Lan;

    private static string RouteWord(PcRoute route) => route switch { PcRoute.Tailscale => "Tailscale", PcRoute.Address => "Other address", _ => "LAN" };

    /// The chip's tooltip: the status sentence, or what the chip is.
    public string ConnectTip
    {
        get
        {
            var tip = Connection.Tip is { Length: > 0 } status ? status : "How this PC is reached.";
            return _service.RouteNote(Id) is { Length: > 0 } note ? $"{tip} {note}" : tip;
        }
    }

    public bool ShowsAddress => Route == PcRoute.Address;

    /// The address typed for a PC on ZeroTier or another network; applied when the box is left.
    [ObservableProperty] private string _address = string.Empty;

    /// The Connect menu: the routes, each with its live result (only the chosen route is measured), a thin line and "Check now".
    public IReadOnlyList<MenuRowItem> ConnectRows => new[]
    {
        MenuRowItem.Header("Connect via"),
        RouteRow(PcRoute.Lan, "Found on the local network by itself."),
        RouteRow(PcRoute.Tailscale, "Reached over Tailscale; this page checks what is still needed."),
        RouteRow(PcRoute.Address, "Reached at an address on another network such as ZeroTier; type it below."),
        new MenuRowItem("Check now", string.Empty, false, "Measure the chosen route again.", new RelayCommand(() => _service.CheckRouteNow(Id)), RuleAbove: true),
    };

    private MenuRowItem RouteRow(PcRoute route, string tip)
    {
        var result = route == Route && _service.ProbeOf(Id) is { } probe && probe.Route == route ? probe.Result : null;
        var tags = result is null ? null : new[] { result.Ok ? new TagItem($"{result.Ms} ms", TagTone.Good) : new TagItem("not reachable", TagTone.Neutral, result.Reason) };
        return new MenuRowItem(RouteWord(route), string.Empty, route == Route, tip, new RelayCommand(() => SetRoute(route)), Tags: tags);
    }

    private void SetRoute(PcRoute route)
    {
        _service.SetRoute(Id, route, Address);
        Refresh();
    }

    /// The address box was left or confirmed: the PC is reached there from now on.
    public void ApplyAddress()
    {
        if (Route == PcRoute.Address) _service.SetRoute(Id, Route, Address);
    }

    // ---- What the service knows ----

    /// Reads the online state, the tags and the note again.
    public void Refresh()
    {
        IsOnline = _service.IsOnline(Id);
        IsLoading = IsOnline && !_service.HasLiveCatalog(Id);
        OnPropertyChanged(nameof(ShowsGroups));
        // The tag says what a probe over the chosen route really found (an authenticated ping answered by that PC), never what is only chosen.
        var probe = _service.ProbeOf(Id);
        var connected = _service.RouteConnected(Id);
        var word = RouteWord(Route);
        NeedsTailscale = _service.NeedsTailscaleSetup(Id);
        // The chosen route is never swapped silently: a failed one is said in the chip, and the one fall-back that exists (Tailscale to LAN) names both.
        TagItem state;
        if (connected) state = new TagItem($"{word} · {probe!.Result.Ms} ms", TagTone.Good, $"Reached over {word}.");
        else if (NeedsTailscale) state = IsOnline
            ? new TagItem("LAN (Tailscale not reachable)", TagTone.Caution, probe is { Result.Ok: false, Route: PcRoute.Tailscale } ? probe.Result.Reason : "Tailscale is set but does not carry the link yet; it stays on LAN meanwhile.")
            : new TagItem("Tailscale · not reachable", TagTone.Caution, probe is { Result.Ok: false, Route: PcRoute.Tailscale } ? probe.Result.Reason : null);
        else if (probe is { Result.Ok: false } && IsOnline) state = new TagItem($"{word} · not reachable", TagTone.Caution, probe.Result.Reason);
        else if (probe is { Result.Ok: false }) state = OfflineTag($"{word}: {probe.Result.Reason}");
        else state = IsOnline ? new TagItem("Checking…", TagTone.Neutral, "Waiting for the first check over this route.") : OfflineTag(null);
        Connection = state;
        RefreshWake();
        RefreshCountdown();
        TabRoute = connected ? word : IsOnline ? "LAN" : "offline";
        TabTip = state.Tip is { Length: > 0 } tip ? $"{Name}: {state.Text}. {tip}" : $"{Name}: {state.Text}";
        Checks.Clear();
        if (NeedsTailscale)
            foreach (var check in _service.PairChecklist(Id)) Checks.Add(new CheckItem(check, checkId => _service.RunPairTailscaleFix(Id, checkId)));
        SwitchProblem = NeedsTailscale ? _service.PairSwitchProblem(Id) : string.Empty;
        ShowsChecklist = NeedsTailscale && (Checks.Any(c => c.IsFailed) || HasSwitchProblem);
        RefreshReach();
        OnPropertyChanged(nameof(CheckText));
        Note = connected || IsOnline ? string.Empty
            : !_service.HasKey(Id) ? "Its pairing key is missing on this PC. Unpair it and pair again."
            : probe is { Result.Ok: false } ? $"Not connected: {probe.Result.Reason}"
            : "Not connected. It connects by itself when that PC is on and can be reached.";
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(RealName));
        OnPropertyChanged(nameof(HasNickname));
        OnPropertyChanged(nameof(ConnectTip));
        OnPropertyChanged(nameof(ShowsAddress));
    }

    /// Makes the sections again from what the PC shares now.
    public void Rebuild()
    {
        Mirror.Rebuild();
        IsLoading = IsOnline && !_service.HasLiveCatalog(Id);
        RefreshCountdown();
    }

    /// One state's value changed: its card shows it at once.
    public void Update(StateEntry state) => Mirror.Update(state);

    /// The rows of the PC's menu, ONE source for the tab's right-click menu and the header's "⋯" pill.
    public IReadOnlyList<MenuRowItem> MenuRows
    {
        get
        {
            var rows = new List<MenuRowItem>
            {
                new("Give nickname…", string.Empty, false, "Name this PC here; the other PC is never told.", new AsyncRelayCommand(GiveNicknameAsync), Icon: "pencil"),
                new("Set up wake…", string.Empty, false, "Check and turn on what waking this PC over the network needs.", SetUpWakeCommand, Icon: "power"),
                MenuRowItem.Sub(PcAllowances.Title(Name), "lock", () => PcAllowances.All.Select(a => AllowRow(a.What, a.Label, a.Tip)).ToList()),
            };
            if (IsOnline)
                rows.Add(new MenuRowItem($"Import settings from {Name}…", string.Empty, false, "Copy chosen areas of that PC's settings here, after a backup of this PC. No passwords are sent.", ImportSettingsCommand, Icon: "download"));
            rows.Add(new MenuRowItem("Unpair", string.Empty, false, "Forget this PC. It keeps its side until it is unpaired there too.", UnpairCommand, RuleAbove: true, Icon: "trash", IsDanger: true));
            return rows;
        }
    }

    private MenuRowItem AllowRow(PcAllowance what, string text, string tip) =>
        new(text, string.Empty, _service.Allows(Id, what), tip, new RelayCommand(() => _service.SetAllows(Id, what, !_service.Allows(Id, what))), IsSwitch: true, KeepsOpen: true);

    /// "Give nickname…": asks for the name (empty removes it); kept on this PC only.
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
