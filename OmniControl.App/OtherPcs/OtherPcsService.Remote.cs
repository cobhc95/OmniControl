using Avalonia.Threading;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.App.States;

namespace OmniControl.App.OtherPcs;

/// <summary>One state another PC shares, as the Other PCs page draws it: the master-list entry (live value, and a way to set it when <see cref="Settable"/>), the card it is in, the part of the page ("LG 27"), its name without that part, and whether it is a screen's power switch.</summary>
public sealed record RemoteState(StateEntry Entry, string Common, string Section, string Name, bool Settable, bool ScreenPower);

/// <summary>One action another PC shares: the master-list entry (running it asks that PC), the card it is in, whether it turns every screen off and whether it is confirmed first.</summary>
public sealed record RemoteAction(HotkeyAction Action, string Common, bool ScreensOff, bool ConfirmRun);

// What other PCs share: each PC's catalog is registered in this PC's master lists as "PC: item" (so hotkeys, the mouse, gestures,
// automation and search use them like any other), kept up to date while the link lives and removed when it ends or the PC is unpaired.
// What a PC asks this one to run is checked against what is shared with it, rate limited, and started through HotkeyRegistry.Run.
public sealed partial class OtherPcsService
{
    private const int ItemsMax = 3000, TextMax = 120, IdMax = 200;

    /// <summary>What one PC has registered here: each item's structure (to see what changed) by its own id.</summary>
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
    private readonly Dictionary<int, string> _pending = new();
    private readonly Dictionary<string, LaunchLimiter> _runLimits = new();
    private readonly Dictionary<string, LaunchLimiter> _setLimits = new();
    private readonly Dictionary<string, (string PcId, string ItemId, StateValue Value)> _setQueue = new();
    private DispatcherTimer? _setTimer;
    private int _nextRid;

    /// <summary>True once the connected PC has sent what it shares (until then the page shows a loading state, not the last saved set).</summary>
    public bool HasLiveCatalog(string pcId) => _liveCatalogs.Contains(pcId);

    /// <summary>A PC's shared items were registered, changed or removed (the page shows them again).</summary>
    public event Action? RemoteChanged;

    /// <summary>A shared state of another PC changed its value.</summary>
    public event Action<StateEntry>? RemoteValueChanged;

    /// <summary>The states the PC shares now, from the master list (live values through <see cref="StateRegistry"/>).</summary>
    public IReadOnlyList<RemoteState> StatesOf(string pcId)
    {
        var prefix = PcProtocol.RemotePrefixOf(pcId);
        if (!_remote.TryGetValue(pcId, out var set)) return Array.Empty<RemoteState>();
        var list = new List<RemoteState>();
        foreach (var entry in _states.States.Where(s => s.Id.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (!set.StateInfo.TryGetValue(entry.Id[prefix.Length..], out var info)) continue;
            var section = SectionOf(Clip(info.Group), info.Heading);
            var name = Clip(info.Name);
            if (section.Length > 0 && name.EndsWith($" ({section})", StringComparison.Ordinal)) name = name[..^(section.Length + 3)];
            list.Add(new RemoteState(entry, Clip(info.Common, 40), section, name, info.Settable, info.ScreenPower));
        }
        return list;
    }

    /// <summary>The part of a page a heading names ("Displays - LG 27" on the page "Displays" is "LG 27"); empty for the page itself.</summary>
    private static string SectionOf(string page, string? heading)
    {
        var prefix = Services.Heading.Join(page, "x")[..^1];
        var clean = Clip(heading);
        return clean.StartsWith(prefix, StringComparison.Ordinal) ? clean[prefix.Length..] : string.Empty;
    }

    /// <summary>The actions the PC shares now, from the master list.</summary>
    public IReadOnlyList<RemoteAction> ActionsOf(string pcId)
    {
        var prefix = PcProtocol.RemotePrefixOf(pcId);
        if (!_remote.TryGetValue(pcId, out var set)) return Array.Empty<RemoteAction>();
        return _hotkeys.Actions.Where(a => a.Id.StartsWith(prefix, StringComparison.Ordinal))
            .Select(a => (Action: a, Info: set.ActionInfo.GetValueOrDefault(a.Id[prefix.Length..])))
            .Where(x => x.Info is not null)
            .Select(x => new RemoteAction(x.Action, Clip(x.Info!.Common, 40), x.Info.ScreensOff, x.Info.ConfirmRun)).ToList();
    }

    private static string Clip(string? text, int max = TextMax)
    {
        var clean = new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > max ? clean[..max] : clean;
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
            case PcMsgType.Ran: HandleRan(message); break;
            case PcMsgType.Devices: HandleDevicesRequest(link, message); break;
            case PcMsgType.DevicesAnswer: HandleDevicesAnswer(link, message); break;
            case PcMsgType.WakeInfo: HandleWakeInfo(link.PeerId, message); break;
            case PcMsgType.Leaving: HandleLeaving(link.PeerId, message); break;
            case PcMsgType.WakeAsk: HandleWakeAsk(link, message); break;
            case PcMsgType.WakeAnswer: HandleWakeAnswer(link, message); break;
            case PcMsgType.TailscaleAddress: HandleTailscaleAddress(link.PeerId, message.Text); break;
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
            _remoteValues[remoteId] = new StateValue(s.Number, s.Text is null ? null : Clip(s.Text));
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

    /// <summary>Registers what each paired PC shared when it was last connected, so its entries are listed (unavailable) before it connects again.</summary>
    private void RestoreCatalogs()
    {
        foreach (var pair in _settings.Pairs.Where(p => p.Catalog is not null).ToList())
        {
            ApplyCatalog(pair.Id, new PcMsg { T = PcMsgType.Catalog, States = pair.Catalog!.States, Actions = pair.Catalog.Actions });
            SetAvailable(pair.Id, false);
        }
    }

    /// <summary>Lists what a PC shares again under its current display name (the nickname changed); the values and the link are kept.</summary>
    private void Reregister(string pcId)
    {
        if (Find(pcId) is not { } pair || !_remote.TryGetValue(pcId, out var set)) return;
        _states.Unregister(set.StateInfo.Keys.Select(id => PcProtocol.RemoteId(pcId, id)).ToList());
        foreach (var id in set.ActionInfo.Keys) _hotkeys.Unregister(PcProtocol.RemoteId(pcId, id));
        _states.Register(set.StateInfo.Values.Select(s => MakeState(pair, PcProtocol.RemoteId(pcId, s.Id), s)).ToList());
        _hotkeys.Register(set.ActionInfo.Values.Select(a => MakeAction(pair, a)).ToList());
        RegisterWake(pair);
        SetAvailable(pcId, IsOnline(pcId));
        RemoteChanged?.Invoke();
    }

    /// <summary>A PC's actions are unavailable, and its states say they are only the last known, while it is not connected.</summary>
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

    private static string StructureOf(SharedAction a) => string.Join('\u001f', Clip(a.Title), a.Icon, a.ScreensOff, a.ConfirmRun, a.Common);

    private StateEntry MakeState(PairedPc pair, string remoteId, SharedState s) =>
        new(remoteId, Clip(s.Name), Category, (StateKind)s.Kind, () => _remoteValues.GetValueOrDefault(remoteId),
            $"Reported live by {pair.DisplayName} over the network{(s.Settable ? string.Empty : " (read only)")}.", Heading: pair.DisplayName, Min: s.Min, Max: s.Max, Unit: Clip(s.Unit, 12),
            Choices: s.Choices is { } choices ? () => choices.Select(c => Clip(c)).ToList() : null, Keywords: $"other pc remote {pair.DisplayName}",
            Set: s.Settable ? value => SendSet(pair.Id, s.Id, value) : null, SuggestedIcons: SuggestedFrom(s.Icon), ScreenPower: s.ScreenPower,
            Liveness: () => new StateLiveness(false, false, null, IsOnline(pair.Id) ? null : $"{pair.DisplayName} is not connected: this is the last value it reported."),
            RemotePc: LabelOf(pair));

    /// <summary>The mark of an entry of this PC in the lists: read from the pairing each time, so a rename follows.</summary>
    private static RemotePcLabel LabelOf(PairedPc pair) => new(Category, () => pair.DisplayName, () => pair.Nickname.Length > 0 ? pair.Name : null);

    private static IReadOnlyList<string>? SuggestedFrom(string? icon) => icon is null ? null : new[] { Clip(icon, 60) };

    private HotkeyAction MakeAction(PairedPc pair, SharedAction a)
    {
        var pcId = pair.Id;
        var itemId = a.Id;
        return new HotkeyAction(PcProtocol.RemoteId(pcId, itemId), Clip(a.Title), Category, $"other pc remote run {pair.DisplayName}",
            _ => RunRemote(pcId, itemId), group: pair.DisplayName) { RemotePc = LabelOf(pair), SuggestedIcons = SuggestedFrom(a.Icon), ScreensOff = a.ScreensOff, UnavailableReason = IsOnline(pcId) ? null : $"{pair.DisplayName} is not connected." };
    }

    private void ApplyState(string pcId, PcMsg message)
    {
        if (message.Id is not { Length: > 0 and <= IdMax } id) return;
        var remoteId = PcProtocol.RemoteId(pcId, id);
        if (!_remoteValues.ContainsKey(remoteId)) return; // not in what was shared: ignored
        _remoteValues[remoteId] = new StateValue(message.Number, message.Text is null ? null : Clip(message.Text));
        _states.Notify(remoteId);
    }

    /// <summary>Takes everything a PC shared out of the master lists (its link ended, or it was unpaired).</summary>
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
        var rid = ++_nextRid;
        _pending[rid] = pair.DisplayName;
        _ = link.SendAsync(new PcMsg { T = PcMsgType.Run, Id = actionId, Rid = rid, Chain = _hotkeys.Chain?.Rules.ToList() });
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
            var rid = ++_nextRid;
            _pending[rid] = pair.DisplayName;
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

    /// <summary>Why a change must not be made (null: it may): the state must be one shared with this PC that has a way to be set, the value one it can have, and the rate within the limit.</summary>
    private string? SetRefusal(PairedPc pair, PcMsg message, out StateEntry? entry, out StateValue? value)
    {
        entry = null;
        value = null;
        if (message.Id is not { Length: > 0 and <= IdMax } id || PcProtocol.IsRemote(id) || _states.Find(id) is not { Set: not null, Common: not null } found)
            return "That state is not one that may be changed from another PC.";
        value = found.Kind switch
        {
            StateKind.OnOff when message.Number is 0 or 1 => StateValue.Of(message.Number == 1),
            StateKind.Level when message.Number is { } n && double.IsFinite(n) => StateValue.Of(Math.Clamp(n, found.Min, found.Max)),
            StateKind.Choice when message.Text is { } text && found.Choices?.Invoke().Contains(text) == true => StateValue.Of(text),
            _ => null,
        };
        if (value is null) return "That value is not one this state can have.";
        if (!(_setLimits.TryGetValue(pair.Id, out var limit) ? limit : _setLimits[pair.Id] = new LaunchLimiter(Timing.PcSetBurstMax, Timing.PcSetBurstWindow)).TryAcquire())
            return "Too many changes were asked for at once, so this one was not made.";
        entry = found;
        return null;
    }

    /// <summary>Runs one of another PC's shared actions as any action runs here (the gate, the chain), which sends it to that PC.</summary>
    public void Run(HotkeyAction action) => _hotkeys.Run(action.Id);

    private void HandleRan(PcMsg message)
    {
        if (message.Rid is { } rid && _pending.Remove(rid, out var name) && message.Ok == false)
            _report($"{name}: {Clip(message.Why, 200)}");
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
            _ = Task.Run(() =>
            {
                _hotkeys.RunWithin(chain!);
                _hotkeys.Run(action!.Id);
            });
        }
        _ = link.SendAsync(reply);
    }

    /// <summary>Why a request must not run (null: it may): the action must be one of those shared with this PC, the chain of rules short enough and the rate within the limit.</summary>
    private string? Refusal(PairedPc pair, PcMsg message, out HotkeyAction? action, out RunChain? chain)
    {
        action = null;
        chain = null;
        if (message.Id is not { Length: > 0 and <= IdMax } id || PcProtocol.IsRemote(id) || _hotkeys.Find(id) is not { Common: not null } found)
            return "That action is not one that may be run from another PC.";
        var peerRule = PcProtocol.RemotePrefix + pair.Id;
        var incoming = message.Chain is { Count: > 0 } rules ? new RunChain(rules.Take(Timing.AutomationChainMax * 2).Select(r => Clip(r, 80)).ToList()) : null;
        if (!RunChain.MayStart(incoming, peerRule, Timing.AutomationChainMax))
            return "Actions started each other too many times, so this one was not run.";
        if (!(_runLimits.TryGetValue(pair.Id, out var limit) ? limit : _runLimits[pair.Id] = new LaunchLimiter(Timing.PcRunBurstMax, Timing.PcRunBurstWindow)).TryAcquire())
            return "Too many actions were asked for at once, so this one was not run.";
        action = found;
        chain = RunChain.After(incoming, peerRule, byPerson: incoming is null); // no chain: a person on that PC asked
        return null;
    }
}
