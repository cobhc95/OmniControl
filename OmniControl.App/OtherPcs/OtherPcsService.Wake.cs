using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.OtherPcs;

/// <summary>Where a wake of a paired PC stands: nothing running, the signal being sent and waited for (<see cref="WakeStatus.Tries"/> sent so far), or given up.</summary>
public enum WakePhase { Idle, Waking, Failed }

/// <summary>The state of a wake: <see cref="Why"/> is the one plain sentence (a tooltip) when it failed.</summary>
public sealed record WakeStatus(WakePhase Phase, int Tries, string? Why);

/// <summary>What another PC answered to a wake request: whether it did it and, when not, one plain sentence why.</summary>
public sealed record WakeReply(bool Ok, string Why);

// Waking a paired PC over the network (docs/mockups/other-pc-wake.html, set-up-wake.html). This part is the PC that wants the other one on:
// what each PC reports about being wakeable (wake-info, kept on its pair), the notice it sends before it sleeps or shuts down (leaving), the
// wake itself (the magic packet from here when this PC is on its network, else relayed by another online paired PC there; retried; honest on
// failure) and the requests for a check, a fix or a test (answered behind that PC's own prompt, see OtherPcsService.WakeHere.cs).
public sealed partial class OtherPcsService
{
    private readonly Dictionary<string, WakeStatus> _wake = new();
    private readonly Dictionary<int, (string PcId, TaskCompletionSource<PcMsg> Answer)> _wakeAsks = new();
    private long _lastLeaving;

    private static string WakeActionId(string pcId) => $"otherpcs.wake.{pcId}";

    /// <summary>Where the wake of a PC stands.</summary>
    public WakeStatus WakeStatusOf(string id) => _wake.GetValueOrDefault(id) ?? new WakeStatus(WakePhase.Idle, 0, null);

    /// <summary>What can honestly be said about waking the PC now (its header's tag and pill).</summary>
    internal WakeAdvice WakeAdviceOf(string id) => Find(id) is { } pair
        ? WakeAdvices.Of(pair.Wake, IsOnline(id), pair.Away, WakePathExists(pair), pair.DisplayName)
        : new WakeAdvice(WakeState.None, string.Empty);

    /// <summary>How the PC reported it can be woken (null: never).</summary>
    public WakeFacts? WakeFactsOf(string id) => Find(id)?.Wake;

    /// <summary>How this PC itself reported it can be woken (null: not read yet).</summary>
    public WakeFacts? OwnWakeFacts => _ownWake;

    /// <summary>What can honestly be said about waking this PC from the network (its tag on the page's title line).</summary>
    internal WakeAdvice OwnWakeAdvice(string name) => WakeAdvices.Of(_ownWake, true, null, true, name);

    /// <summary>Reads this PC's wake facts if they are old or were never read (off the UI thread; <see cref="Changed"/> follows when they changed).</summary>
    public void ReadOwnWake() => RefreshOwnWake();

    /// <summary>Reads this PC's wake facts again now.</summary>
    public async Task<WakeReply> CheckOwnWakeAsync(CancellationToken stop = default)
    {
        await Task.WhenAny(ReadOwnWakeAsync(), Until(stop));
        RaiseChanged();
        return new WakeReply(true, string.Empty);
    }

    /// <summary>Turns the settings on for this PC; Windows asks for administrator rights first and nothing changes without a yes.</summary>
    public async Task<WakeReply> FixOwnWakeAsync(IReadOnlyList<WakeFix> fixes, CancellationToken stop = default)
    {
        var (reply, _) = await ApplyWakeFixesHereAsync(fixes, stop);
        RaiseChanged();
        return reply;
    }

    /// <summary>Puts back what OmniControl changed on this PC (Windows asks for administrator rights; nothing changes without a yes).</summary>
    public async Task<WakeReply> UndoOwnWakeAsync()
    {
        var (reply, _) = await UndoWakeHereAsync();
        RaiseChanged();
        return reply;
    }

    /// <summary>How the PC went away, from its notice: the words and when (null: no notice came).</summary>
    public (string Away, DateTime At)? AwayOf(string id) => Find(id) is { Away: { } away, AwayAt: { } at } ? (away, at) : null;

    /// <summary>True when this PC is on the PC's network, or another online paired PC is (it then sends the signal for this one).</summary>
    private bool WakePathExists(PairedPc pair) => pair.Wake is { Subnet.Length: > 0 } facts && (OwnWakeSubnet == facts.Subnet || RelayFor(pair) is not null);

    private string? OwnWakeSubnet => _ownWake is { Known: true } own ? own.Subnet : null;

    private PairedPc? RelayFor(PairedPc pair) => _settings.Pairs.FirstOrDefault(p => p.Id != pair.Id && IsOnline(p.Id) && p.Wake is { Known: true } w && w.Subnet == pair.Wake?.Subnet);

    private void SetWake(string pcId, WakeStatus status)
    {
        if (status.Phase == WakePhase.Idle) _wake.Remove(pcId); else _wake[pcId] = status;
        RaiseChanged();
    }

    // ---- The "Wake <PC>" master action, one per paired PC ----

    private void RegisterWakeActions()
    {
        foreach (var pair in _settings.Pairs) RegisterWake(pair);
    }

    /// <summary>Registers (or registers again under a new name) the master action that wakes the PC; its id stays, so keys bound to it stay.</summary>
    private void RegisterWake(PairedPc pair)
    {
        var id = pair.Id;
        _hotkeys.Unregister(WakeActionId(id));
        _hotkeys.Register(new HotkeyAction(WakeActionId(id), $"Wake {pair.DisplayName}", Category, $"wake turn on power start boot wol {pair.DisplayName}", keys => { _ = Guard.RunAsync("Other PCs: wake", () => WakeAsync(id)); }, group: pair.DisplayName)
            { RemotePc = LabelOf(pair), SuggestedIcons = new[] { "power" } });
    }

    // ---- What a PC reports, and the notice before it goes away ----

    /// <summary>A link came up: the PC is on (any earlier notice and failed wake are over), and this PC tells it how it can be woken.</summary>
    private void OnWakeLinkUp(PcLink link, PairedPc pair)
    {
        if (pair.Away is not null)
        {
            pair.Away = null;
            pair.AwayAt = null;
            Save();
        }
        _wake.Remove(pair.Id);
        if (_ownWake is { } facts) _ = link.SendAsync(new PcMsg { T = PcMsgType.WakeInfo, Wake = facts });
        RefreshOwnWake();
    }

    private void HandleWakeInfo(string pcId, PcMsg message)
    {
        if (Find(pcId) is not { } pair || message.Wake is not { } reported) return;
        var clean = Cleaned(reported);
        if (pair.Wake?.Key == clean.Key) return;
        pair.Wake = clean;
        Save();
        RaiseChanged();
    }

    /// <summary>What another PC reported, with text cut and anything that is not an address or network emptied.</summary>
    private static WakeFacts Cleaned(WakeFacts facts)
    {
        var mac = MagicPacket.ParseMac(facts.Mac) is null ? string.Empty : Clip(facts.Mac, 17);
        var subnet = MagicPacket.BroadcastOf(facts.Subnet) is null ? string.Empty : Clip(facts.Subnet, 20);
        return new WakeFacts
        {
            Known = facts.Known && mac.Length > 0 && subnet.Length > 0, Adapter = Clip(facts.Adapter, 80), Mac = mac, Subnet = subnet, CardCanWake = facts.CardCanWake,
            MagicPacketOn = facts.MagicPacketOn, Armed = facts.Armed, MagicOnly = facts.MagicOnly, FastStartupOn = facts.FastStartupOn, ModernStandby = facts.ModernStandby, CanUndo = facts.CanUndo,
        };
    }

    private void HandleLeaving(string pcId, PcMsg message)
    {
        if (Find(pcId) is not { } pair || !WakeAdvices.IsAway(message.Text)) return;
        pair.Away = message.Text;
        pair.AwayAt = DateTime.Now;
        Save();
        RaiseChanged();
    }

    /// <summary>
    /// This PC is about to sleep, hibernate or shut down: every connected PC is told, so its tag says exactly how this one went away. Called
    /// on the power listener's thread while Windows waits a moment; it waits at most <see cref="Timing.WakeNoticeWait"/> for the notices to leave.
    /// </summary>
    public void AnnounceLeaving(PowerEventKind kind)
    {
        var word = kind switch { PowerEventKind.Suspend => WakeAdvices.Sleep, PowerEventKind.Hibernate => WakeAdvices.Hibernate, PowerEventKind.Shutdown => WakeAdvices.Shutdown, _ => null };
        var now = Environment.TickCount64;
        if (word is null || _disposed || now - Interlocked.Exchange(ref _lastLeaving, now) < Timing.PowerEventSame.TotalMilliseconds) return; // the same event heard on two channels
        Task[] sent;
        Func<Task[]> send = () => _links.Values.Select(l => (Task)l.SendAsync(new PcMsg { T = PcMsgType.Leaving, Text = word })).ToArray();
        if (Dispatcher.UIThread.CheckAccess()) sent = send();
        else
        {
            var queued = Dispatcher.UIThread.InvokeAsync(send).GetTask();
            if (!queued.Wait(Timing.WakeNoticeWait)) return;
            sent = queued.Result;
        }
        Task.WaitAll(sent, Timing.WakeNoticeWait);
    }

    // ---- This PC's own facts ----

    private WakeFacts? _ownWake;
    private DateTime _ownWakeAt;
    private bool _readingWake;

    /// <summary>Reads this PC's wake facts again when they are older than <see cref="Timing.WakeFactsEvery"/> (or <paramref name="force"/>), off the UI thread; connected PCs are told when they changed.</summary>
    private void RefreshOwnWake(bool force = false)
    {
        if (_readingWake || _disposed || StartupMode.Inert || (!force && DateTime.UtcNow - _ownWakeAt < Timing.WakeFactsEvery)) return;
        _readingWake = true;
        OffUi("Other PCs: reading wake facts", async () =>
        {
            try
            {
                var facts = await Task.Run(ReadWakeFacts).ConfigureAwait(false);
                Post(() => SetOwnWake(facts));
            }
            finally
            {
                Post(() => _readingWake = false); // however the read ended, the next one may start
            }
        });
    }

    private static void LogWakeRead(string text) => AppDataStore.AppendLog(Guard.LogFile, text);

    private void SetOwnWake(WakeFacts facts)
    {
        _readingWake = false;
        _ownWakeAt = DateTime.UtcNow;
        if (_ownWake?.Key == facts.Key) return;
        _ownWake = facts;
        foreach (var link in _links.Values) _ = link.SendAsync(new PcMsg { T = PcMsgType.WakeInfo, Wake = facts });
        RaiseChanged(); // whether another PC can be reached from this network may have changed
    }

    // ---- Waking ----

    /// <summary>Wakes a PC that is off and wakeable; says one plain sentence when it is already on or cannot be. Returns how it ended.</summary>
    public async Task<WakeStatus> WakeAsync(string pcId)
    {
        if (Find(pcId) is not { } pair) return new WakeStatus(WakePhase.Failed, 0, "That PC is not paired.");
        if (IsOnline(pcId))
        {
            _report($"{pair.DisplayName} is already on.");
            return WakeStatusOf(pcId);
        }
        if (WakeStatusOf(pcId).Phase == WakePhase.Waking) return WakeStatusOf(pcId);
        var advice = WakeAdviceOf(pcId);
        if (advice.State is not (WakeState.Wakeable or WakeState.Unconfirmed))
        {
            _report(advice.Reason.Length > 0 ? advice.Reason : $"{pair.DisplayName} can't be woken remotely.");
            return WakeStatusOf(pcId);
        }
        var sent = 0;
        var why = string.Empty;
        while (sent < Timing.WakeTries)
        {
            SetWake(pcId, new WakeStatus(WakePhase.Waking, sent, null));
            if (Find(pcId) is not { } now) return WakeStatusOf(pcId);
            var problem = await SendWakeSignalAsync(now);
            if (problem is not null)
            {
                why = problem;
                break;
            }
            sent++;
            SetWake(pcId, new WakeStatus(WakePhase.Waking, sent, null));
            if (await WaitOnlineAsync(pcId, Timing.WakeWaitEach))
            {
                SetWake(pcId, new WakeStatus(WakePhase.Idle, sent, null));
                _report($"{pair.DisplayName} is on.");
                return WakeStatusOf(pcId);
            }
            why = $"It did not answer within {Timing.WakeWaitEach.TotalSeconds:0} seconds.";
        }
        Guard.Log("Other PCs: wake", new IOException($"{pair.DisplayName} did not wake after {sent} tries: {why}"));
        var failed = new WakeStatus(WakePhase.Failed, sent, why);
        SetWake(pcId, failed);
        return failed;
    }

    private async Task<bool> WaitOnlineAsync(string pcId, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until && !_disposed)
        {
            if (IsOnline(pcId)) return true;
            await Task.Delay(Timing.WakePoll);
        }
        return IsOnline(pcId);
    }

    /// <summary>Sends one wake signal: from this PC when it is on the PC's network, else through another online paired PC there. Null = sent; else one plain sentence why not.</summary>
    private async Task<string?> SendWakeSignalAsync(PairedPc pair)
    {
        if (pair.Wake is not { Known: true } facts) return "It has not told this PC its network card yet.";
        if (OwnWakeSubnet == facts.Subnet)
        {
            var (ok, why) = await Task.Run(() =>
            {
                var sent = MagicPacket.Send(facts.Mac, facts.Subnet, out var reason);
                return (sent, reason);
            });
            return ok ? null : why;
        }
        if (RelayFor(pair) is not { } relay) return "No PC on its network is on to send the wake signal.";
        var reply = await AskWakeAsync(relay.Id, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeRelay, Text = facts.Mac, Payload = facts.Subnet }, Timing.WakeAskWait);
        return reply is { Ok: true } ? null : Clip(reply?.Why, 200) is { Length: > 0 } text ? text : $"{relay.DisplayName} did not answer.";
    }

    // ---- Asking another PC (check, fix, test, relay) ----

    private async Task<PcMsg?> AskWakeAsync(string pcId, PcMsg ask, TimeSpan wait, CancellationToken stop = default)
    {
        if (!_links.TryGetValue(pcId, out var link)) return null;
        var rid = ++_nextRid;
        ask.Rid = rid;
        var answer = new TaskCompletionSource<PcMsg>(TaskCreationOptions.RunContinuationsAsynchronously);
        _wakeAsks[rid] = (pcId, answer);
        try
        {
            if (!await link.SendAsync(ask)) return null;
            return await Task.WhenAny(answer.Task, Task.Delay(wait, stop)) == answer.Task ? answer.Task.Result : null;
        }
        finally
        {
            _wakeAsks.Remove(rid);
        }
    }

    private void HandleWakeAnswer(PcLink link, PcMsg message)
    {
        if (message.Rid is not { } rid || !_wakeAsks.TryGetValue(rid, out var pending) || pending.PcId != link.PeerId) return;
        if (message.Wake is not null) HandleWakeInfo(link.PeerId, message);
        pending.Answer.TrySetResult(message);
    }

    private WakeReply ReplyOf(PcMsg? answer, string name) => answer is null ? new WakeReply(false, $"{name} did not answer.")
        : new WakeReply(answer.Ok == true, answer.Ok == true ? string.Empty : Clip(answer.Why, 200) is { Length: > 0 } why ? why : $"{name} could not do it.");

    /// <summary>Asks a connected PC to read how it can be woken again.</summary>
    public async Task<WakeReply> CheckWakeAsync(string pcId, CancellationToken stop = default) =>
        ReplyOf(await AskWakeAsync(pcId, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeCheck }, Timing.WakeAskWait, stop), Find(pcId)?.DisplayName ?? "That PC");

    /// <summary>Asks a connected PC to turn the settings on. It shows its own prompt, and Windows asks it once more; nothing changes unless a person there allows both.</summary>
    public async Task<WakeReply> FixWakeAsync(string pcId, IEnumerable<WakeFix> fixes, CancellationToken stop = default) =>
        ReplyOf(await AskWakeAsync(pcId, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeFix, Text = string.Join(',', fixes.Select(f => f.ToString())) }, Timing.WakeFixWait, stop), Find(pcId)?.DisplayName ?? "That PC");

    /// <summary>Asks a connected PC to put back what OmniControl turned on there (behind its own prompt).</summary>
    public async Task<WakeReply> UndoWakeAsync(string pcId) =>
        ReplyOf(await AskWakeAsync(pcId, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeUndo }, Timing.WakeFixWait), Find(pcId)?.DisplayName ?? "That PC");

    /// <summary>
    /// The test: asks the PC to go to sleep (it shows its own prompt), waits for it to go, then wakes it from here. The words say what really
    /// happened at each step.
    /// </summary>
    public async Task<string> TestWakeAsync(string pcId)
    {
        var name = Find(pcId)?.DisplayName ?? "That PC";
        var reply = ReplyOf(await AskWakeAsync(pcId, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeTest }, Timing.WakeFixWait), name);
        if (!reply.Ok) return reply.Why;
        if (!await WaitOfflineAsync(pcId, Timing.WakeSleepWait)) return $"{name} did not go to sleep.";
        var status = await WakeAsync(pcId);
        return status.Phase == WakePhase.Failed ? $"The test failed: {WakeFailedWords(status)}." : IsOnline(pcId) ? $"The test worked: {name} slept and woke." : status.Why ?? $"{name} did not wake.";
    }

    private async Task<bool> WaitOfflineAsync(string pcId, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until && !_disposed)
        {
            if (!IsOnline(pcId)) return true;
            await Task.Delay(Timing.WakePoll);
        }
        return !IsOnline(pcId);
    }

    /// <summary>The words of a failed wake: "Didn't wake (tried 3 times)", or that the signal could not be sent at all.</summary>
    public static string WakeFailedWords(WakeStatus status) => status.Tries == 0 ? "Couldn't send the wake signal"
        : $"Didn't wake (tried {status.Tries} {(status.Tries == 1 ? "time" : "times")})";
}
