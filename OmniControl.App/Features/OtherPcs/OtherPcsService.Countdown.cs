using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.OtherPcs;

/// Another PC's power countdown as this PC shows it: the action, its words, when it ends on this PC's clock and how long it lasts in all.
public sealed record PcCountdown(RemoteAction Action, CountdownWords Words, DateTime Ends, TimeSpan Length)
{
    /// The time left as "m:ss", whole seconds rounded up.
    public static string Clock(TimeSpan left)
    {
        var seconds = (int)Math.Ceiling(Math.Max(0, left.TotalSeconds));
        return $"{seconds / 60}:{seconds % 60:00}";
    }
}

// Power countdowns between paired PCs: this PC tells every connected PC when one of its own starts, runs out or ends; a PC shows
// another's countdown only for an action it asked that PC to run, until that PC says it ended or connects again.
public sealed partial class OtherPcsService
{
    private readonly Dictionary<string, string> _askedPower = new(); // pc id -> its id of the countdown action asked last
    private readonly Dictionary<string, (string ItemId, DateTime Ends, TimeSpan Length)> _countdowns = new();

    /// The power countdown of this PC changed (its id).
    public event Action<string>? CountdownChanged;

    /// The countdown of an action this PC asked that PC to run; null when none shows.
    public PcCountdown? CountdownOf(string pcId)
    {
        if (!_countdowns.TryGetValue(pcId, out var now)) return null;
        var id = PcProtocol.RemoteId(pcId, now.ItemId);
        if (ActionsOf(pcId).FirstOrDefault(a => a.Action.Id == id) is not { } action) return null;
        var title = action.Action.Title;
        return new PcCountdown(action, _hotkeys.Find(now.ItemId)?.CountdownWords ?? new CountdownWords(title, $"{title}…"), now.Ends, now.Length);
    }

    /// Cancels that PC's countdown through its shared Cancel action (the one run path); its countdown clears when that PC says it ended.
    public void CancelCountdown(string pcId)
    {
        if (ActionsOf(pcId).FirstOrDefault(a => a.CancelsCountdown) is { } cancel) Run(cancel.Action);
    }

    /// This PC's own countdown started, ran out or ended: every connected PC is told (any thread).
    public void AnnounceCountdown(PowerCountdown? now) => PcWork.Post(() =>
    {
        var message = new PcMsg { T = PcMsgType.Countdown, Id = now?.ActionId, LeftMs = now is null ? null : (int)now.Left.TotalMilliseconds };
        foreach (var link in _links.Values.ToList()) _ = link.SendAsync(message);
    });

    private void AskedPower(string pcId, string actionId)
    {
        if (_remote.TryGetValue(pcId, out var set) && set.ActionInfo.GetValueOrDefault(actionId)?.Countdown is not null) _askedPower[pcId] = actionId;
    }

    private void HandleCountdown(string pcId, PcMsg message)
    {
        if (message.Id is null) ClearCountdown(pcId);
        else if (message.LeftMs is { } ms && _askedPower.GetValueOrDefault(pcId) == message.Id)
        {
            var left = TimeSpan.FromMilliseconds(Math.Clamp(ms, 0, Timing.PowerCountdownMaxSeconds * 1000));
            var length = _remote.TryGetValue(pcId, out var set) && set.ActionInfo.GetValueOrDefault(message.Id)?.Countdown is { } seconds
                ? Timing.PowerCountdownOf(seconds) : left;
            _countdowns[pcId] = (message.Id, DateTime.UtcNow + left, length);
            CountdownChanged?.Invoke(pcId);
        }
    }

    /// That PC's countdown ended there, it connected again or it was unpaired: nothing of it shows any more.
    private void ClearCountdown(string pcId)
    {
        _askedPower.Remove(pcId);
        if (_countdowns.Remove(pcId)) CountdownChanged?.Invoke(pcId);
    }
}
