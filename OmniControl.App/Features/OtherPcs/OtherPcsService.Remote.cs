using OmniControl.App.Shared.Contracts;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.OtherPcs;

/// One state another PC shares, as the Other PCs page draws it: the master-list entry (live value, and a way to set it when <see cref="Settable"/>), the card it is in, the part of the page ("LG 27"), its name without that part, and whether it is a screen's power switch.
public sealed record RemoteState(StateEntry Entry, string Common, string Section, string Name, bool Settable, bool ScreenPower, string? Compact = null, OnOffIcons? StateIcons = null);

/// One action another PC shares: the master-list entry (running it asks that PC), the card it is in, whether it turns every screen off, whether it is confirmed first,
/// the seconds of its power countdown there (null: none, or that PC does not say) and whether it cancels that PC's countdown.
public sealed record RemoteAction(HotkeyAction Action, string Common, bool ScreensOff, bool ConfirmRun, int? Countdown = null, bool CancelsCountdown = false);

// What other PCs share: each PC's catalog is registered in this PC's master lists as "PC: item" (so hotkeys, the mouse, gestures,
// automation and search use them like any other), kept up to date while the link lives and removed when it ends or the PC is unpaired.
// What a PC asks this one to run is checked against what is shared with it, rate limited, and started through HotkeyRegistry.Run.
public sealed partial class OtherPcsService
{
    private const int ItemsMax = 3000, IdMax = 200;

    /// What one PC has registered here: each item's structure (to see what changed) by its own id.
    private sealed class RemoteSet
    {
        public readonly Dictionary<string, string> States = new();
        public readonly Dictionary<string, string> Actions = new();
        public readonly Dictionary<string, SharedState> StateInfo = new();
        public readonly Dictionary<string, SharedAction> ActionInfo = new();
    }

    private readonly Dictionary<string, RemoteSet> _remote = new();
    private readonly HashSet<string> _liveCatalogs = new(); // PCs whose catalog arrived over the link now up (not the saved one)
    private readonly Dictionary<string, StateValue?> _remoteValues = new();
    private readonly Dictionary<(string PcId, int Rid), string> _pending = new(); // a run or set sent, by the PC it went to and its number: only that PC's answer is taken
    private readonly PcLimits _runLimits = new(Timing.PcRunBurstMax, Timing.PcRunBurstWindow);
    private readonly PcLimits _setLimits = new(Timing.PcSetBurstMax, Timing.PcSetBurstWindow);
    private readonly Dictionary<string, (string PcId, string ItemId, StateValue Value)> _setQueue = new();
    private DispatcherTimer? _setTimer;

    /// True once the connected PC has sent what it shares (until then the page shows a loading state, not the last saved set).
    public bool HasLiveCatalog(string pcId) => _liveCatalogs.Contains(pcId);

    /// A PC's shared items were registered, changed or removed (the page shows them again).
    public event Action? RemoteChanged;

    /// A shared state of another PC changed its value.
    public event Action<StateEntry>? RemoteValueChanged;

    /// The states the PC shares now, from the master list (live values through <see cref="StateRegistry"/>).
    public IReadOnlyList<RemoteState> StatesOf(string pcId)
    {
        var prefix = PcProtocol.RemotePrefixOf(pcId);
        if (!_remote.TryGetValue(pcId, out var set)) return Array.Empty<RemoteState>();
        var list = new List<RemoteState>();
        // By id, never by arrival order, so the cards of a group keep their places when the catalog is sent again.
        foreach (var entry in _states.States.Where(s => s.Id.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            if (!set.StateInfo.TryGetValue(entry.Id[prefix.Length..], out var info)) continue;
            var section = SectionOf(PcProtocol.Clip(info.Group), info.Heading);
            // This PC's own entry of the same id names it when it has one, so a PC on an older build still shows today's wording.
            var own = _states.Find(info.Id);
            var name = own?.Name ?? PcProtocol.Clip(info.Name);
            if (section.Length > 0 && name.EndsWith($" ({section})", StringComparison.Ordinal)) name = name[..^(section.Length + 3)];
            list.Add(new RemoteState(entry, PcProtocol.Clip(info.Common, 40), section, name, info.Settable, info.ScreenPower, own?.Compact, own?.StateIcons));
        }
        return list;
    }

    /// The part of a page a heading names ("Displays - LG 27" on the page "Displays" is "LG 27"); empty for the page itself.
    private static string SectionOf(string page, string? heading)
    {
        var prefix = Shared.Services.Heading.Join(page, "x")[..^1];
        var clean = PcProtocol.Clip(heading);
        return clean.StartsWith(prefix, StringComparison.Ordinal) ? clean[prefix.Length..] : string.Empty;
    }

    /// The actions the PC shares now, from the master list.
    public IReadOnlyList<RemoteAction> ActionsOf(string pcId)
    {
        var prefix = PcProtocol.RemotePrefixOf(pcId);
        if (!_remote.TryGetValue(pcId, out var set)) return Array.Empty<RemoteAction>();
        return _hotkeys.Actions.Where(a => a.Id.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => (Action: a, Info: set.ActionInfo.GetValueOrDefault(a.Id[prefix.Length..])))
            .Where(x => x.Info is not null)
            .Select(x => new RemoteAction(x.Action, PcProtocol.Clip(x.Info!.Common, 40), x.Info.ScreensOff, x.Info.ConfirmRun,
                x.Info.Countdown is { } seconds ? (int)Timing.PowerCountdownOf(seconds).TotalSeconds : null,
                _hotkeys.Find(x.Info.Id)?.CancelsCountdown == true)).ToList(); // what this PC's own entry of the same id is

    }

    private void OnMessage(PcLink link, PcMsg message)
    {
        if (_disposed || _links.GetValueOrDefault(link.PeerId) != link) return;
        switch (message.T)
        {
            case PcMsgType.Catalog:
                _liveCatalogs.Add(link.PeerId);
                ApplyCatalog(link.PeerId, message);
                break;
            case PcMsgType.State: ApplyState(link.PeerId, message); break;
            case PcMsgType.Set: HandleSet(link, message); break;
            case PcMsgType.Run: HandleRun(link, message); break;
            case PcMsgType.Ran: HandleRan(link, message); break;
            case PcMsgType.Devices: HandleDevicesRequest(link, message); break;
            case PcMsgType.ImportList or PcMsgType.ImportGet: HandleImportRequest(link, message); break;
            case PcMsgType.DevicesAnswer or PcMsgType.ImportAnswer: _requests.Answer(link, message); break;
            case PcMsgType.WakeInfo: Wake.HandleInfo(link.PeerId, message); break;
            case PcMsgType.Leaving: Wake.HandleLeaving(link.PeerId, message); break;
            case PcMsgType.WakeAsk: Wake.HandleAsk(link, message); break;
            case PcMsgType.WakeAnswer: Wake.HandleAnswer(link, message); break;
            case PcMsgType.TailscaleAddress: HandleTailscaleAddress(link.PeerId, message.Text); break;
            case PcMsgType.Route: HandleRoute(link.PeerId, message.Text); break;
            case PcMsgType.Countdown: HandleCountdown(link.PeerId, message); break;
            // A ping only keeps the link from looking silent; anything else is ignored.
        }
    }

    // ---- Registering what the other PC shares ----

    private void ApplyCatalog(string pcId, PcMsg message)
    {
        if (Find(pcId) is not { } pair) return;
        var set = _remote.TryGetValue(pcId, out var known) ? known : _remote[pcId] = new RemoteSet();

        var states = (message.States ?? new()).Where(s => !string.IsNullOrEmpty(s.Id) && s.Id.Length <= IdMax && Enum.IsDefined((StateKind)s.Kind)).DistinctBy(s => s.Id).Take(ItemsMax).ToList();
        var actions = (message.Actions ?? new()).Where(a => !string.IsNullOrEmpty(a.Id) && a.Id.Length <= IdMax).DistinctBy(a => a.Id).Take(ItemsMax).ToList();

        var goneStates = set.States.Keys.Except(states.Select(s => s.Id)).ToList();
        _states.Unregister(goneStates.Select(id => PcProtocol.RemoteId(pcId, id)));
        foreach (var id in goneStates)
        {
            set.States.Remove(id);
            set.StateInfo.Remove(id);
            _remoteValues.Remove(PcProtocol.RemoteId(pcId, id));
        }
        var changedStates = new List<StateEntry>();
        foreach (var s in states)
        {
            var remoteId = PcProtocol.RemoteId(pcId, s.Id);
            set.StateInfo[s.Id] = s;
            _remoteValues[remoteId] = new StateValue(s.Number, s.Text is null ? null : PcProtocol.Clip(s.Text), Subject: s.Subject is null ? null : PcProtocol.Clip(s.Subject));
            var key = StructureOf(s);
            if (set.States.TryGetValue(s.Id, out var was) && was == key)
            {
                _states.Notify(remoteId); // the value may differ: the registry compares
                continue;
            }
            set.States[s.Id] = key;
            changedStates.Add(MakeState(pair, remoteId, s));
        }
        if (changedStates.Count > 0) _states.Register(changedStates);

        var goneActions = set.Actions.Keys.Except(actions.Select(a => a.Id)).ToList();
        var changedActions = actions.Where(a => !set.Actions.TryGetValue(a.Id, out var was) || was != StructureOf(a)).ToList();
        foreach (var id in goneActions.Concat(changedActions.Select(a => a.Id)))
        {
            _hotkeys.Unregister(PcProtocol.RemoteId(pcId, id));
            set.Actions.Remove(id);
            set.ActionInfo.Remove(id);
        }
        foreach (var a in changedActions)
        {
            set.Actions[a.Id] = StructureOf(a);
            set.ActionInfo[a.Id] = a;
        }
        if (changedActions.Count > 0) _hotkeys.Register(changedActions.Select(a => MakeAction(pair, a)).ToList());
        SetAvailable(pcId, true);
        pair.Catalog = new PcCatalog { States = states, Actions = actions };
        Save();
        RemoteChanged?.Invoke();
    }

    /// Registers what each paired PC shared when it was last connected, so its entries are listed (unavailable) before it connects again.
    private void RestoreCatalogs()
    {
        foreach (var pair in _settings.Pairs.Where(p => p.Catalog is not null).ToList())
        {
            ApplyCatalog(pair.Id, new PcMsg { T = PcMsgType.Catalog, States = pair.Catalog!.States, Actions = pair.Catalog.Actions });
            SetAvailable(pair.Id, false);
        }
    }

    /// Lists what a PC shares again under its current display name (the nickname changed); the values and the link are kept.
    private void Reregister(string pcId)
    {
        if (Find(pcId) is not { } pair || !_remote.TryGetValue(pcId, out var set)) return;
        _states.Unregister(set.StateInfo.Keys.Select(id => PcProtocol.RemoteId(pcId, id)).ToList());
        foreach (var id in set.ActionInfo.Keys) _hotkeys.Unregister(PcProtocol.RemoteId(pcId, id));
        _states.Register(set.StateInfo.Values.Select(s => MakeState(pair, PcProtocol.RemoteId(pcId, s.Id), s)).ToList());
        _hotkeys.Register(set.ActionInfo.Values.Select(a => MakeAction(pair, a)).ToList());
        Wake.Register(pair);
        SetAvailable(pcId, IsOnline(pcId));
        RemoteChanged?.Invoke();
    }

    /// A PC's actions are unavailable, and its states say they are only the last known, while it is not connected.
    private void SetAvailable(string pcId, bool online)
    {
        if (!online) _liveCatalogs.Remove(pcId);
        if (!_remote.TryGetValue(pcId, out var set)) return;
        var name = Find(pcId)?.DisplayName ?? "That PC";
        foreach (var id in set.Actions.Keys)
            if (_hotkeys.Find(PcProtocol.RemoteId(pcId, id)) is { } action) action.UnavailableReason = online ? null : $"{name} is not connected.";
        foreach (var id in set.States.Keys) _states.NotifyLiveness(PcProtocol.RemoteId(pcId, id));
    }

    private static string StructureOf(SharedState s) =>
        string.Join('\u001f', s.Name, s.Group, s.Heading, s.Icon, s.Settable, s.ScreenPower, s.Common, s.Kind, s.Min, s.Max, s.Unit, string.Join('\u001e', s.Choices ?? new List<string>()));

    private static string StructureOf(SharedAction a) => string.Join('\u001f', PcProtocol.Clip(a.Title), a.Icon, a.ScreensOff, a.ConfirmRun, a.Common, a.Countdown);

    private StateEntry MakeState(PairedPc pair, string remoteId, SharedState s) =>
        new(remoteId, PcProtocol.Clip(s.Name), Category, (StateKind)s.Kind, () => _remoteValues.GetValueOrDefault(remoteId),
            $"Reported live by {pair.DisplayName} over the network{(s.Settable ? string.Empty : " (read only)")}.", Heading: pair.DisplayName, Min: s.Min, Max: s.Max, Unit: PcProtocol.Clip(s.Unit, 12),
            Choices: s.Choices is { } choices ? () => choices.Select(c => PcProtocol.Clip(c)).ToList() : null, Keywords: $"other pc remote {pair.DisplayName}",
            Set: s.Settable ? value => SendSet(pair.Id, s.Id, value) : null, SuggestedIcons: SuggestedFrom(s.Icon), ScreenPower: s.ScreenPower,
            Liveness: () => new StateLiveness(false, false, null, IsOnline(pair.Id) ? null : $"{pair.DisplayName} is not connected: this is the last value it reported."),
            RemotePc: LabelOf(pair));

    /// The mark of an entry of this PC in the lists: read from the pairing each time, so a rename follows.
    internal static RemotePcLabel LabelOf(PairedPc pair) => new(Category, () => pair.DisplayName, () => pair.Nickname.Length > 0 ? pair.Name : null);

    private static IReadOnlyList<string>? SuggestedFrom(string? icon) => icon is null ? null : new[] { PcProtocol.Clip(icon, 60) };

    private HotkeyAction MakeAction(PairedPc pair, SharedAction a)
    {
        var pcId = pair.Id;
        var itemId = a.Id;
        return new HotkeyAction(PcProtocol.RemoteId(pcId, itemId), PcProtocol.Clip(a.Title), Category, $"other pc remote run {pair.DisplayName}",
            _ => RunRemote(pcId, itemId), group: pair.DisplayName) { RemotePc = LabelOf(pair), SuggestedIcons = SuggestedFrom(a.Icon), ScreensOff = a.ScreensOff, UnavailableReason = IsOnline(pcId) ? null : $"{pair.DisplayName} is not connected." };
    }

    private void ApplyState(string pcId, PcMsg message)
    {
        if (message.Id is not { Length: > 0 and <= IdMax } id) return;
        var remoteId = PcProtocol.RemoteId(pcId, id);
        if (!_remoteValues.ContainsKey(remoteId)) return; // not in what was shared: ignored
        _remoteValues[remoteId] = new StateValue(message.Number, message.Text is null ? null : PcProtocol.Clip(message.Text), Subject: message.Subject is null ? null : PcProtocol.Clip(message.Subject));
        _states.Notify(remoteId);
    }

    /// Takes everything a PC shared out of the master lists (its link ended, or it was unpaired).
    private void ForgetRemote(string pcId)
    {
        if (!_remote.Remove(pcId, out var set)) return;
        var states = set.States.Keys.Select(id => PcProtocol.RemoteId(pcId, id)).ToList();
        _states.Unregister(states);
        foreach (var id in states) _remoteValues.Remove(id);
        foreach (var queued in _setQueue.Where(q => q.Value.PcId == pcId).Select(q => q.Key).ToList()) _setQueue.Remove(queued);
        foreach (var id in set.Actions.Keys) _hotkeys.Unregister(PcProtocol.RemoteId(pcId, id));
        RemoteChanged?.Invoke();
    }

    // ---- Running an action on the other PC, and the other PC running one here ----

    private void RunRemote(string pcId, string actionId)
    {
        if (Find(pcId) is not { } pair || !_links.TryGetValue(pcId, out var link))
        {
            _report($"{Find(pcId)?.DisplayName ?? "That PC"} is not connected, so the action was not sent.");
            return;
        }
        if (_pending.Count >= 64) _pending.Clear(); // answers that never came
        var rid = _requests.Next();
        _pending[(pcId, rid)] = pair.DisplayName;
        AskedPower(pcId, actionId);
        _ = link.SendAsync(new PcMsg { T = PcMsgType.Run, Id = actionId, Rid = rid, Chain = _run.Chain?.Rules.ToList() });
    }

    // ---- Setting a state on the other PC, and the other PC setting one here ----

    // A value set here (a dragged slider) shows at once, and only the newest value per state is sent, at most every Timing.PcSetEvery.
    private void SendSet(string pcId, string itemId, StateValue value)
    {
        var remoteId = PcProtocol.RemoteId(pcId, itemId);
        if (!_links.ContainsKey(pcId))
        {
            _report($"{Find(pcId)?.DisplayName ?? "That PC"} is not connected, so the change was not sent.");
            return;
        }
        _remoteValues[remoteId] = value;
        _states.Notify(remoteId);
        _setQueue[remoteId] = (pcId, itemId, value);
        _setTimer ??= new DispatcherTimer(Timing.PcSetEvery, DispatcherPriority.Background, (_, _) => Guard.Run("Other PCs: sending a change", FlushSets));
        if (!_setTimer.IsEnabled) _setTimer.Start();
    }

    private void FlushSets()
    {
        _setTimer?.Stop();
        var queued = _setQueue.Values.ToList();
        _setQueue.Clear();
        foreach (var (pcId, itemId, value) in queued)
        {
            if (Find(pcId) is not { } pair || !_links.TryGetValue(pcId, out var link)) continue;
            if (_pending.Count >= 64) _pending.Clear();
            var rid = _requests.Next();
            _pending[(pcId, rid)] = pair.DisplayName;
            _ = link.SendAsync(new PcMsg { T = PcMsgType.Set, Id = itemId, Number = value.Number, Text = value.Text, Rid = rid });
        }
    }

    private void HandleSet(PcLink link, PcMsg message)
    {
        var reply = new PcMsg { T = PcMsgType.Ran, Rid = message.Rid };
        StateEntry? entry = null;
        StateValue? value = null;
        var why = Find(link.PeerId) is { } pair ? SetRefusal(pair, message, out entry, out value) : "This PC is not paired.";
        reply.Ok = why is null;
        reply.Why = why;
        if (why is null) Guard.Run("Other PCs: setting a state", () => entry!.Set!(value!));
        _ = link.SendAsync(reply);
    }

    /// Why a change must not be made (null: it may): the state must be one shared with this PC that has a way to be set, the value one it can have, and the rate within the limit.
    private string? SetRefusal(PairedPc pair, PcMsg message, out StateEntry? entry, out StateValue? value)
    {
        entry = null;
        value = null;
        if (message.Id is not { Length: > 0 and <= IdMax } id || PcProtocol.IsRemote(id) || _states.Find(id) is not { Set: not null, Common: not null } found)
            return "That state is not one that may be changed from another PC.";
        if (!pair.MayRunActions) return $"{_ownName} does not allow {pair.DisplayName} to change its settings.";
        value = found.Kind switch
        {
            StateKind.OnOff when message.Number is 0 or 1 => StateValue.Of(message.Number == 1),
            StateKind.Level when message.Number is { } n && double.IsFinite(n) => StateValue.Of(Math.Clamp(n, found.Min, found.Max)),
            StateKind.Choice when message.Text is { } text && found.Choices?.Invoke().Contains(text) == true => StateValue.Of(text),
            _ => null,
        };
        if (value is null) return "That value is not one this state can have.";
        if (!_setLimits.TryAcquire(pair.Id))
            return "Too many changes were asked for at once, so this one was not made.";
        entry = found;
        return null;
    }

    /// Runs one of another PC's shared actions as any action runs here (the gate, the chain), which sends it to that PC.
    public void Run(HotkeyAction action) => _run.Run(action.Id);

    private void HandleRan(PcLink link, PcMsg message)
    {
        if (message.Rid is { } rid && _pending.Remove((link.PeerId, rid), out var name) && message.Ok == false)
            _report($"{name}: {PcProtocol.Clip(message.Why, 200)}");
    }

    private void HandleRun(PcLink link, PcMsg message)
    {
        var reply = new PcMsg { T = PcMsgType.Ran, Rid = message.Rid };
        HotkeyAction? action = null;
        RunChain? chain = null;
        var why = Find(link.PeerId) is { } pair ? Refusal(pair, message, out action, out chain) : "This PC is not paired.";
        if (why is not null)
        {
            reply.Ok = false;
            reply.Why = why;
        }
        else
        {
            reply.Ok = true;
            // On a worker, so the chain set for this one run never stays on the UI thread; Run reads it on the calling thread.
            Guard.OffUi("Other PCs: running an asked action", () =>
            {
                _run.RunWithin(chain!);
                _run.Run(action!.Id);
            });
        }
        _ = link.SendAsync(reply);
    }

    /// Why a request must not run (null: it may): the action must be one of those shared with this PC, the chain of rules short enough and the rate within the limit.
    private string? Refusal(PairedPc pair, PcMsg message, out HotkeyAction? action, out RunChain? chain)
    {
        action = null;
        chain = null;
        if (message.Id is not { Length: > 0 and <= IdMax } id || PcProtocol.IsRemote(id) || _hotkeys.Find(id) is not { Common: not null } found)
            return "That action is not one that may be run from another PC.";
        if (!found.CancelsCountdown && !(found.Common == CommonGroups.Power ? pair.MayRunPower : pair.MayRunActions))
            return $"{_ownName} does not allow {pair.DisplayName} to run {(found.Common == CommonGroups.Power ? "power actions" : "its actions")}.";
        var peerRule = PcProtocol.RemotePrefix + pair.Id;
        var incoming = message.Chain is { Count: > 0 } rules ? new RunChain(rules.Take(Timing.AutomationChainMax * 2).Select(r => PcProtocol.Clip(r, 80)).ToList()) : null;
        if (!RunChain.MayStart(incoming, peerRule, Timing.AutomationChainMax))
            return "Actions started each other too many times, so this one was not run.";
        if (!_runLimits.TryAcquire(pair.Id))
            return "Too many actions were asked for at once, so this one was not run.";
        action = found;
        chain = RunChain.After(incoming, peerRule, byPerson: false); // never a person's request here, whoever asked there (power waits out its grace)
        return null;
    }
}
