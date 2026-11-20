using OmniControl.App.Features.Hotkeys;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Features.OtherPcs;

/// Where a wake of a paired PC stands: nothing running, the signal being sent and waited for, or given up.
public enum WakePhase { Idle, Waking, Failed }

/// The state of a wake: whether its one signal was <see cref="Sent"/>; <see cref="Why"/> is the one plain sentence (a tooltip) when it failed.
public sealed record WakeStatus(WakePhase Phase, bool Sent, string? Why);

/// What another PC answered to a wake request: whether it did it and, when not, one plain sentence why.
public sealed record WakeReply(bool Ok, string Why);

/// Waking paired PCs over the network, both sides. As the PC that wants another one on: what each PC reports about being wakeable (kept on
/// its pair), the notice it sends before it sleeps or shuts down, the "Wake PC" master action and the wake itself (one signal per person's press,
/// through <see cref="WakeGate"/>: the magic packet from here when this PC is on its network, else relayed by another online paired PC there;
/// honest on failure), and the requests for a check, a fix or a test. As the PC asked: relaying a press made there, reading its facts again, fixing
/// and the sleep test, at most Timing.WakeAskBurstMax times per window; a fix and a test change this PC, so each shows this PC's own prompt first
/// (and Windows asks for administrator rights once more for a fix): nothing changes unless a person here says yes. UI thread, like the rest of Other PCs.
public sealed class PcWake : IDisposable
{
    private const string NotPressed = "Only pressing Wake sends the wake signal.";

    private readonly IPcBook _book;
    private readonly PcRequests _requests;
    private readonly IActionRegistry _actions;
    private readonly Action<string> _report;
    private readonly Func<DialogMessage, Task<bool>> _askHere;
    private readonly string _ownName;
    private readonly WakeGate _gate;
    private readonly Func<WakeFacts> _readOwn;
    private readonly Dictionary<string, WakeStatus> _wake = new();
    private readonly PcLimits _askLimits = new(Timing.WakeAskBurstMax, Timing.WakeAskBurstWindow);
    private readonly WakeUndoLog _undo = new();
    private Task _fixRun = Task.CompletedTask; // the elevated change that is running now, so an undo never starts before it ended
    private WakeFacts? _ownWake;
    private DateTime _ownWakeAt;
    private bool _readingWake;
    private long _lastLeaving;
    private bool _disposed;

    /// <param name="readOwn">Reads this PC's own wake facts from Windows (slow: called off the UI thread).</param>
    internal PcWake(IPcBook book, PcRequests requests, IActionRegistry actions, Action<string> report, Func<DialogMessage, Task<bool>> askHere, string ownName, WakeGate gate, Func<WakeFacts>? readOwn = null)
    {
        _book = book;
        _requests = requests;
        _actions = actions;
        _report = report;
        _askHere = askHere;
        _ownName = ownName;
        _gate = gate;
        _readOwn = readOwn ?? WakeReader.Read;
        WakeReader.Failed += LogWakeRead;
        WakeReader.Answered += LogWakeRead;
    }

    private static string ActionId(string pcId) => $"otherpcs.wake.{pcId}";

    /// Where the wake of a PC stands.
    public WakeStatus WakeStatusOf(string id) => _wake.GetValueOrDefault(id) ?? new WakeStatus(WakePhase.Idle, false, null);

    /// What can honestly be said about waking the PC now (its header's tag and pill).
    internal WakeAdvice WakeAdviceOf(string id) => _book.Find(id) is { } pair
        ? WakeAdvices.Of(pair.Wake, _book.IsOnline(id), pair.Away, WakePathExists(pair), pair.DisplayName)
        : new WakeAdvice(WakeState.None, string.Empty);

    /// How the PC reported it can be woken (null: never).
    public WakeFacts? WakeFactsOf(string id) => _book.Find(id)?.Wake;

    /// How this PC itself reported it can be woken (null: not read yet).
    public WakeFacts? OwnWakeFacts => _ownWake;

    /// What can honestly be said about waking this PC from the network (its tag on the page's title line).
    internal WakeAdvice OwnWakeAdvice(string name) => WakeAdvices.Of(_ownWake, true, null, true, name);

    /// Reads this PC's wake facts if they are old or were never read (off the UI thread; the page's change event follows when they changed).
    public void ReadOwnWake() => RefreshOwnWake();

    /// Reads this PC's wake facts again now.
    public async Task<WakeReply> CheckOwnWakeAsync(CancellationToken stop = default)
    {
        await Task.WhenAny(ReadOwnWakeAsync(), PcWork.Until(stop));
        _book.RaiseChanged();
        return new WakeReply(true, string.Empty);
    }

    /// Turns the settings on for this PC; Windows asks for administrator rights first and nothing changes without a yes.
    public async Task<WakeReply> FixOwnWakeAsync(IReadOnlyList<WakeFix> fixes, CancellationToken stop = default)
    {
        var (reply, _) = await ApplyFixesHereAsync(fixes, stop);
        _book.RaiseChanged();
        return reply;
    }

    /// Puts back what OmniControl changed on this PC (Windows asks for administrator rights; nothing changes without a yes).
    public async Task<WakeReply> UndoOwnWakeAsync()
    {
        var (reply, _) = await UndoHereAsync();
        _book.RaiseChanged();
        return reply;
    }

    /// How the PC went away, from its notice: the words and when (null: no notice came).
    public (string Away, DateTime At)? AwayOf(string id) => _book.Find(id) is { Away: { } away, AwayAt: { } at } ? (away, at) : null;

    /// True when this PC is on the PC's network, or another online paired PC is (it then sends the signal for this one).
    private bool WakePathExists(PairedPc pair) => pair.Wake is { Subnet.Length: > 0 } facts && (OwnWakeSubnet == facts.Subnet || RelayFor(pair) is not null);

    private string? OwnWakeSubnet => _ownWake is { Known: true } own ? own.Subnet : null;

    private PairedPc? RelayFor(PairedPc pair) => _book.Pairs.FirstOrDefault(p => p.Id != pair.Id && _book.IsOnline(p.Id) && p.Wake is { Known: true } w && w.Subnet == pair.Wake?.Subnet);

    private void SetWake(string pcId, WakeStatus status)
    {
        if (status.Phase == WakePhase.Idle) _wake.Remove(pcId); else _wake[pcId] = status;
        _book.RaiseChanged();
    }

    // ---- The "Wake <PC>" master action, one per paired PC ----

    /// Registers (or registers again under a new name) the master action that wakes the PC; its id stays, so keys bound to it stay. It is
    /// <see cref="HotkeyAction.PersonOnly"/>: the run path lets only a person's key, button or click through, so its run is that person's press.
    internal void Register(PairedPc pair)
    {
        var id = pair.Id;
        _actions.Unregister(ActionId(id));
        _actions.Register(new HotkeyAction(ActionId(id), $"Wake {pair.DisplayName}", OtherPcsService.Category, $"wake turn on power start boot wol {pair.DisplayName}", keys => { _ = Guard.RunAsync("Other PCs: wake", () => WakeAsync(id, GrantWake(id))); }, group: pair.DisplayName)
            { RemotePc = OtherPcsService.LabelOf(pair), SuggestedIcons = new[] { "power" }, PersonOnly = true });
    }

    /// A person pressed Wake for this PC (its button, or its key): the one-shot go-ahead its wake needs.
    public WakeGrant GrantWake(string pcId) => _gate.Grant(pcId, Timing.WakeGrantLife);

    /// A person pressed Test wake for this PC: the go-ahead for the one wake that ends the test.
    public WakeGrant GrantTest(string pcId) => _gate.Grant(pcId, Timing.WakeTestGrantLife);

    internal void Unregister(string pcId) => _actions.Unregister(ActionId(pcId));

    // ---- What a PC reports, and the notice before it goes away ----

    /// A link came up: the PC is on (any earlier notice and failed wake are over), and this PC tells it how it can be woken.
    internal void LinkUp(PcLink link, PairedPc pair)
    {
        ClearAway(pair);
        _wake.Remove(pair.Id);
        if (_ownWake is { } facts) _ = link.SendAsync(new PcMsg { T = PcMsgType.WakeInfo, Wake = facts });
        RefreshOwnWake();
    }

    /// The PC was heard by other means (its beacon, an answered check) while it has no link: it is on, so its notice is over; true when one was
    /// cleared. While its old link is open the notice is newer than what was heard (a PC still beacons for a moment after it said it sleeps).
    internal bool HeardAgain(PairedPc pair) => !_book.IsOnline(pair.Id) && ClearAway(pair);

    private bool ClearAway(PairedPc pair)
    {
        if (pair.Away is null) return false;
        pair.Away = null;
        pair.AwayAt = null;
        _book.Save();
        return true;
    }

    internal void HandleInfo(string pcId, PcMsg message)
    {
        if (_book.Find(pcId) is not { } pair || message.Wake is not { } reported) return;
        var clean = Cleaned(reported);
        if (pair.Wake?.Key == clean.Key) return;
        pair.Wake = clean;
        _book.Save();
        _book.RaiseChanged();
    }

    /// What another PC reported, with text cut and anything that is not an address or network emptied.
    private static WakeFacts Cleaned(WakeFacts facts)
    {
        var mac = MagicPacket.ParseMac(facts.Mac) is null ? string.Empty : PcProtocol.Clip(facts.Mac, 17);
        var subnet = MagicPacket.BroadcastOf(facts.Subnet) is null ? string.Empty : PcProtocol.Clip(facts.Subnet, 20);
        return new WakeFacts
        {
            Known = facts.Known && mac.Length > 0 && subnet.Length > 0, Adapter = PcProtocol.Clip(facts.Adapter, 80), Mac = mac, Subnet = subnet, CardCanWake = facts.CardCanWake,
            MagicPacketOn = facts.MagicPacketOn, Armed = facts.Armed, MagicOnly = facts.MagicOnly, FastStartupOn = facts.FastStartupOn, ModernStandby = facts.ModernStandby, CanUndo = facts.CanUndo,
        };
    }

    internal void HandleLeaving(string pcId, PcMsg message)
    {
        if (_book.Find(pcId) is not { } pair || !WakeAdvices.IsAway(message.Text)) return;
        pair.Away = message.Text;
        pair.AwayAt = DateTime.Now;
        _book.Save();
        _book.RaiseChanged();
    }

    /// This PC is about to sleep, hibernate or shut down: every connected PC is told, so its tag says exactly how this one went away. Called
    /// on the power listener's thread while Windows waits a moment; it waits at most <see cref="Timing.WakeNoticeWait"/> for the notices to leave.
    public void AnnounceLeaving(PowerEventKind kind)
    {
        var word = kind switch { PowerEventKind.Suspend => WakeAdvices.Sleep, PowerEventKind.Hibernate => WakeAdvices.Hibernate, PowerEventKind.Shutdown => WakeAdvices.Shutdown, _ => null };
        var now = Environment.TickCount64;
        if (word is null || _disposed || now - Interlocked.Exchange(ref _lastLeaving, now) < Timing.PowerEventSame.TotalMilliseconds) return; // the same event heard on two channels
        Task[] sent;
        Func<Task[]> send = () => _book.Links.Select(l => (Task)l.SendAsync(new PcMsg { T = PcMsgType.Leaving, Text = word })).ToArray();
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

    /// Reads this PC's wake facts again when they are older than <see cref="Timing.WakeFactsEvery"/>, off the UI thread; connected PCs are told when they changed.
    internal void RefreshOwnWake()
    {
        if (_readingWake || _disposed || DateTime.UtcNow - _ownWakeAt < Timing.WakeFactsEvery) return;
        _readingWake = true;
        PcWork.OffUi("Other PCs: reading wake facts", async () =>
        {
            try
            {
                var facts = await Task.Run(ReadWakeFacts).ConfigureAwait(false);
                PcWork.Post(() => SetOwnWake(facts));
            }
            finally
            {
                PcWork.Post(() => _readingWake = false); // however the read ended, the next one may start
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
        foreach (var link in _book.Links) _ = link.SendAsync(new PcMsg { T = PcMsgType.WakeInfo, Wake = facts });
        _book.RaiseChanged(); // whether another PC can be reached from this network may have changed
    }

    private async Task<WakeFacts> ReadOwnWakeAsync()
    {
        var facts = await Task.Run(ReadWakeFacts);
        SetOwnWake(facts);
        return facts;
    }

    private WakeFacts ReadWakeFacts()
    {
        var facts = _readOwn();
        facts.CanUndo = _undo.Any;
        return facts;
    }

    // ---- Waking ----

    /// Wakes a PC that is off and wakeable with ONE signal for the person's press (<paramref name="grant"/>), then waits up to
    /// <see cref="Timing.WakeWait"/> for it to connect; says one plain sentence when it is already on or cannot be. Nothing is sent again by
    /// itself: after a failure the person presses Wake again. Returns how it ended.
    public async Task<WakeStatus> WakeAsync(string pcId, WakeGrant? grant)
    {
        if (_book.Find(pcId) is not { } pair) return new WakeStatus(WakePhase.Failed, false, "That PC is not paired.");
        if (_book.IsOnline(pcId))
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
        SetWake(pcId, new WakeStatus(WakePhase.Waking, false, null));
        if (await SendWakeSignalAsync(pair, grant) is { } problem)
        {
            Guard.Log("Other PCs: wake", new IOException($"The wake signal for {pair.DisplayName} was not sent: {problem}"));
            _report($"Couldn't wake {pair.DisplayName}: {problem}");
            var notSent = new WakeStatus(WakePhase.Failed, false, problem);
            SetWake(pcId, notSent);
            return notSent;
        }
        SetWake(pcId, new WakeStatus(WakePhase.Waking, true, null));
        if (await WaitOnlineAsync(pcId, true, Timing.WakeWait))
        {
            SetWake(pcId, new WakeStatus(WakePhase.Idle, true, null));
            _report($"{pair.DisplayName} is on.");
            return WakeStatusOf(pcId);
        }
        var why = $"It did not answer within {Timing.WakeWait.TotalSeconds:0} seconds.";
        Guard.Log("Other PCs: wake", new IOException($"{pair.DisplayName} did not wake: {why}"));
        var failed = new WakeStatus(WakePhase.Failed, true, why);
        SetWake(pcId, failed);
        return failed;
    }

    /// Waits until the PC is connected (<paramref name="online"/>) or not, at most <paramref name="limit"/>; true when it got there.
    private async Task<bool> WaitOnlineAsync(string pcId, bool online, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until && !_disposed)
        {
            if (_book.IsOnline(pcId) == online) return true;
            await Task.Delay(Timing.WakePoll);
        }
        return _book.IsOnline(pcId) == online;
    }

    /// THE dispatch of every wake this PC starts: the person's grant for exactly this PC is used up first, then one signal goes, from this PC when
    /// it is on the PC's network, else through another online paired PC there. Null = sent; else one plain sentence why not.
    private async Task<string?> SendWakeSignalAsync(PairedPc pair, WakeGrant? grant)
    {
        if (!_gate.TryUse(grant, pair.Id)) return NotPressed;
        if (pair.Wake is not { Known: true } facts) return "It has not told this PC its network card yet.";
        if (OwnWakeSubnet == facts.Subnet) return await _gate.SendPacketAsync(facts.Mac, facts.Subnet);
        if (RelayFor(pair) is not { } relay) return "No PC on its network is on to send the wake signal.";
        var reply = await AskAsync(relay.Id, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeRelay, Text = facts.Mac, Payload = facts.Subnet }, Timing.WakeAskWait);
        return reply is { Ok: true } ? null : PcProtocol.Clip(reply?.Why, 200) is { Length: > 0 } text ? text : $"{relay.DisplayName} did not answer.";
    }

    // ---- Asking another PC (check, fix, test, relay) ----

    private Task<PcMsg?> AskAsync(string pcId, PcMsg ask, TimeSpan wait, CancellationToken stop = default) =>
        _book.LinkOf(pcId) is { } link ? _requests.AskAsync(link, ask, wait, stop) : Task.FromResult<PcMsg?>(null);

    internal void HandleAnswer(PcLink link, PcMsg message)
    {
        // The answer's continuation runs later, so the facts it carries are taken before the asker reads them.
        if (_requests.Answer(link, message) && message.Wake is not null) HandleInfo(link.PeerId, message);
    }

    private static WakeReply ReplyOf(PcMsg? answer, string name) => answer is null ? new WakeReply(false, $"{name} did not answer.")
        : new WakeReply(answer.Ok == true, answer.Ok == true ? string.Empty : PcProtocol.Clip(answer.Why, 200) is { Length: > 0 } why ? why : $"{name} could not do it.");

    private string NameOf(string pcId) => _book.Find(pcId)?.DisplayName ?? "That PC";

    /// Asks a connected PC to read how it can be woken again.
    public async Task<WakeReply> CheckWakeAsync(string pcId, CancellationToken stop = default) =>
        ReplyOf(await AskAsync(pcId, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeCheck }, Timing.WakeAskWait, stop), NameOf(pcId));

    /// Asks a connected PC to turn the settings on. It shows its own prompt, and Windows asks it once more; nothing changes unless a person there allows both.
    public async Task<WakeReply> FixWakeAsync(string pcId, IEnumerable<WakeFix> fixes, CancellationToken stop = default) =>
        ReplyOf(await AskAsync(pcId, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeFix, Text = string.Join(',', fixes.Select(f => f.ToString())) }, Timing.WakeFixWait, stop), NameOf(pcId));

    /// Asks a connected PC to put back what OmniControl turned on there (behind its own prompt).
    public async Task<WakeReply> UndoWakeAsync(string pcId) =>
        ReplyOf(await AskAsync(pcId, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeUndo }, Timing.WakeFixWait), NameOf(pcId));

    /// The test (<paramref name="grant"/>: the person's press of Test wake): asks the PC to go to sleep (it shows its own prompt), waits for it to go,
    /// then wakes it from here. The words say what really happened at each step.
    public async Task<string> TestWakeAsync(string pcId, WakeGrant grant)
    {
        var name = NameOf(pcId);
        var reply = ReplyOf(await AskAsync(pcId, new PcMsg { T = PcMsgType.WakeAsk, Id = PcMsgType.WakeTest }, Timing.WakeFixWait), name);
        if (!reply.Ok) return reply.Why;
        if (!await WaitOnlineAsync(pcId, false, Timing.WakeSleepWait)) return $"{name} did not go to sleep.";
        var status = await WakeAsync(pcId, grant);
        return status.Phase == WakePhase.Failed ? $"The test failed: {FailedWords(status)}." : _book.IsOnline(pcId) ? $"The test worked: {name} slept and woke." : status.Why ?? $"{name} did not wake.";
    }

    /// The words of a failed wake: "Didn't wake" after its signal went, or that the signal could not be sent at all.
    public static string FailedWords(WakeStatus status) => status.Sent ? "Didn't wake" : "Couldn't send the wake signal";

    // ---- This PC asked by a paired PC ----

    internal void HandleAsk(PcLink link, PcMsg message)
    {
        var reply = new PcMsg { T = PcMsgType.WakeAnswer, Rid = message.Rid };
        if (_book.Find(link.PeerId) is not { } pair) reply.Why = "This PC is not paired.";
        else if (!_askLimits.TryAcquire(pair.Id)) reply.Why = "Asked too often. Try again in a moment.";
        else
        {
            _ = Guard.RunAsync("Other PCs: wake request", () => AnswerAsync(link, pair, message, reply));
            return;
        }
        _ = link.SendAsync(reply);
    }

    private async Task AnswerAsync(PcLink link, PairedPc pair, PcMsg message, PcMsg reply)
    {
        var sleepAfter = false;
        switch (message.Id)
        {
            case PcMsgType.WakeCheck:
                reply.Wake = await ReadOwnWakeAsync();
                reply.Ok = true;
                break;
            case PcMsgType.WakeRelay:
                await RelayAsync(message, reply);
                break;
            case PcMsgType.WakeFix:
                await FixAskedAsync(pair, message, reply);
                break;
            case PcMsgType.WakeUndo:
                await UndoAskedAsync(pair, reply);
                break;
            case PcMsgType.WakeTest:
                sleepAfter = await TestAskedAsync(pair, reply);
                break;
            default:
                reply.Why = "That is not something this PC does.";
                break;
        }
        await link.SendAsync(reply);
        if (!sleepAfter) return;
        await Task.Delay(Timing.WakeTestDelay); // the answer has left; the notice that this PC sleeps goes out with the sleep itself
        await Task.Run(() => Guard.Run("Other PCs: sleeping for the wake test", () => PowerCommands.Sleep()));
    }

    // A wake signal for a PC on this PC's own network, asked by a paired PC after its person pressed Wake there (only that dispatch sends this
    // ask; the old "relay" of retries and rules is refused); its network and address must be well formed, and the network must be this one.
    private async Task RelayAsync(PcMsg message, PcMsg reply)
    {
        var mac = PcProtocol.Clip(message.Text, 17);
        var subnet = PcProtocol.Clip(message.Payload, 20);
        var own = _ownWake ?? await ReadOwnWakeAsync();
        if (MagicPacket.ParseMac(mac) is null || !own.Known || own.Subnet != subnet)
        {
            reply.Why = "This PC is not on that network.";
            return;
        }
        var why = await _gate.SendPacketAsync(mac, subnet);
        reply.Ok = why is null;
        reply.Why = why;
    }

    private Task<bool> AllowedHereAsync(string title, string text, string yes) => _askHere(new DialogMessage(title, text, yes, YesNo: true));

    private async Task FixAskedAsync(PairedPc pair, PcMsg message, PcMsg reply)
    {
        var fixes = (message.Text ?? string.Empty).Split(',').Take(4).Select(t => (Ok: WakeFixer.TryParse(t, out var fix), Fix: fix)).Where(x => x.Ok).Select(x => x.Fix).Distinct().ToList();
        if (fixes.Count == 0)
        {
            reply.Why = "There was nothing to turn on.";
            return;
        }
        var list = string.Join(", ", fixes.Select(FixWords));
        if (!await AllowedHereAsync($"Set up wake for {pair.DisplayName}", $"{pair.DisplayName} asks to turn on: {list}. Windows will ask once more before it changes.", "Allow"))
        {
            reply.Why = $"{_ownName} did not allow it.";
            return;
        }
        Fill(reply, await ApplyFixesHereAsync(fixes)); // what really changed, row by row
    }

    private async Task UndoAskedAsync(PairedPc pair, PcMsg reply)
    {
        if (!await AllowedHereAsync($"Set up wake for {pair.DisplayName}", $"{pair.DisplayName} asks to put back the wake settings OmniControl changed on this PC. Windows will ask once more before it changes.", "Allow"))
        {
            reply.Why = $"{_ownName} did not allow it.";
            return;
        }
        Fill(reply, await UndoHereAsync());
    }

    private static void Fill(PcMsg reply, (WakeReply Reply, WakeFacts Facts) done)
    {
        reply.Wake = done.Facts;
        reply.Ok = done.Reply.Ok;
        if (!done.Reply.Ok) reply.Why = done.Reply.Why;
    }

    private async Task<bool> TestAskedAsync(PairedPc pair, PcMsg reply)
    {
        if (!await AllowedHereAsync($"Wake test from {pair.DisplayName}", $"{pair.DisplayName} wants to put this PC to sleep, then wake it, to check that waking works. Save your work first.", "Sleep now"))
        {
            reply.Why = $"{_ownName} did not allow it.";
            return false;
        }
        reply.Ok = true;
        return true;
    }

    /// Turns the settings on for this PC, behind Windows' own administrator prompt: the one path for a fix another PC asked for (after this PC's own
    /// question) and for the one asked in this PC's own set-up window. What was off is recorded first so it can be put back. Returns how it ended and
    /// the facts read again afterwards; <paramref name="stop"/> ends the wait at once (the change may still be running: an undo waits for it).
    private async Task<(WakeReply Reply, WakeFacts Facts)> ApplyFixesHereAsync(IReadOnlyList<WakeFix> fixes, CancellationToken stop = default)
    {
        var before = _ownWake is { Known: true } known ? known : await ReadOwnWakeAsync();
        if (stop.IsCancellationRequested) return (new WakeReply(false, "Stopped."), before);
        var added = _undo.Record(fixes, before);
        var run = Task.Run(() => WakeFixer.Apply(fixes, before.Mac));
        _fixRun = run;
        if (await Task.WhenAny(run, PcWork.Until(stop)) != run) return (new WakeReply(false, "Stopped."), before);
        var result = await run;
        if (result.Failure is not (HelperFailure.None or HelperFailure.StillRunning)) _undo.Forget(added); // it never started: nothing to put back
        var after = await ReadOwnWakeAsync();
        return (ReplyOf(result, "change"), after);
    }

    /// Puts back what OmniControl changed on this PC, in one elevated step, then reads again.
    private async Task<(WakeReply Reply, WakeFacts Facts)> UndoHereAsync()
    {
        await Task.WhenAny(_fixRun);
        var changed = _undo.Changed;
        var facts = _ownWake is { Known: true } known ? known : await ReadOwnWakeAsync();
        if (changed.Count == 0) return (new WakeReply(false, "There is nothing to undo."), facts);
        var run = Task.Run(() => WakeFixer.Revert(changed, facts.Mac));
        _fixRun = run;
        var result = await run;
        if (result.Failure == HelperFailure.None && result.ExitCode == 0) _undo.Clear();
        return (ReplyOf(result, "undo"), await ReadOwnWakeAsync());
    }

    private WakeReply ReplyOf(HelperResult result, string what)
    {
        if (result.Failure == HelperFailure.None && result.ExitCode == 0) return new WakeReply(true, string.Empty);
        Guard.Log("Other PCs: wake " + what, new IOException(result.Detail));
        return new WakeReply(false, result.Failure switch
        {
            HelperFailure.Cancelled => $"You said no at the Windows prompt on {_ownName}.",
            HelperFailure.Blocked => "Windows blocked the change.",
            HelperFailure.None => $"Windows could not {what} every setting on {_ownName}.",
            HelperFailure.StillRunning => $"The {what} on {_ownName} did not finish in time.",
            _ => $"The {what} could not be started on {_ownName}.",
        });
    }

    private static string FixWords(WakeFix fix) => fix switch
    {
        WakeFix.MagicPacket => "Wake on Magic Packet",
        WakeFix.Arm => "allow the card to wake this PC",
        WakeFix.MagicOnly => "only a magic packet wakes it",
        _ => "Fast Startup off",
    };

    /// Stops listening to the wake reader and takes every "Wake PC" action out of the master list.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WakeReader.Failed -= LogWakeRead;
        WakeReader.Answered -= LogWakeRead;
        foreach (var pair in _book.Pairs) Unregister(pair.Id);
    }
}
