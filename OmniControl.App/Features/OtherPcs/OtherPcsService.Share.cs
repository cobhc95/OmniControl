using System.Collections.Specialized;
using System.Text.Json;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.OtherPcs;

// What this PC shares: the basic set, every state and action an owner flagged Common on its master entry, read live from the master
// lists (never a copy) and the same for every paired PC. It is sent to a PC when it connects and again whenever the master lists change.
public sealed partial class OtherPcsService
{
    private readonly Dictionary<string, string> _catalogKey = new(); // what each PC was last sent (without values), so only a real change is sent again
    private DispatcherTimer? _settle;

    private void WatchShares()
    {
        _states.ListChanged += ScheduleShare;
        _states.ValueChanged += OnStateChanged;
        _hotkeys.Actions.CollectionChanged += OnActionsChanged;
    }

    private void UnwatchShares()
    {
        _states.ListChanged -= ScheduleShare;
        _states.ValueChanged -= OnStateChanged;
        _hotkeys.Actions.CollectionChanged -= OnActionsChanged;
        _settle?.Stop();
    }

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleShare();

    // Entries register in bursts (a page's actions at start-up, a device's states): one send after they settle.
    private void ScheduleShare()
    {
        if (_disposed) return;
        _settle ??= new DispatcherTimer(Timing.PcShareSettle, DispatcherPriority.Background, (_, _) =>
        {
            _settle!.Stop();
            Guard.Run("Other PCs: sending what is shared", () =>
            {
                foreach (var link in _links.Values.ToList()) SendCatalog(link, force: false);
            });
        });
        _settle.Stop();
        _settle.Start();
    }

    // ---- Sending ----

    private List<SharedState> SharedStates() => _states.States
        .Where(s => s.Common is not null && !PcProtocol.IsRemote(s.Id))
        .Select(s =>
        {
            var now = _states.Now(s.Id);
            return new SharedState(s.Id, s.Name, s.Group, (int)s.Kind, s.Min, s.Max, s.Unit, s.Choices?.Invoke().ToList(), now?.Number, now?.Text,
                s.Heading, SharedIcon(s.ShownIcon), s.Set is not null, s.ScreenPower, s.Common, now?.Subject);
        }).ToList();

    private List<SharedAction> SharedActions() => _hotkeys.Actions
        .Where(a => a.Common is not null && !PcProtocol.IsRemote(a.Id))
        .Select(a => new SharedAction(a.Id, a.Title, SharedIcon(a.ShownIcon), a.Common!, a.ScreensOff, a.ConfirmRun)).ToList();

    /// The icon as the other PC can draw it: a library icon travels, a user's own picture does not (the other PC does not have it).
    private static string? SharedIcon(string? icon) => icon is null || icon.StartsWith(Icons.IconStore.UserPrefix, StringComparison.Ordinal) ? null : icon;

    private void SendCatalog(PcLink link, bool force)
    {
        var states = SharedStates();
        var actions = SharedActions();
        var key = JsonSerializer.Serialize(new { States = states.Select(s => s with { Number = null, Text = null }), actions }, PcProtocol.Json);
        if (!force && _catalogKey.GetValueOrDefault(link.PeerId) == key) return;
        _catalogKey[link.PeerId] = key;
        _ = link.SendAsync(new PcMsg { T = PcMsgType.Catalog, States = states, Actions = actions });
    }

    private void OnStateChanged(StateChange change)
    {
        if (PcProtocol.IsRemote(change.Entry.Id))
        {
            RemoteValueChanged?.Invoke(change.Entry);
            return;
        }
        foreach (var link in _links.Values.ToList())
        {
            if (change.Entry.Common is null) continue;
            _ = link.SendAsync(new PcMsg { T = PcMsgType.State, Id = change.Entry.Id, Number = change.After?.Number, Text = change.After?.Text, Subject = change.After?.Subject });
        }
    }
}
