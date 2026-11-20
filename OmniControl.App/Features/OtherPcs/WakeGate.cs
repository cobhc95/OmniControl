using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Features.OtherPcs;

/// A person's go-ahead to wake one PC: made only by a press (its Wake button, its Test wake row, its "Wake <PC>" key), used once, soon stale, never saved.
public sealed record WakeGrant(string PcId, Guid Id);

/// THE gate of the wake signal: a wake this PC starts is sent only after <see cref="TryUse"/> took a fresh grant for exactly that PC, once.
/// Nothing is queued, retried or kept across a restart. <see cref="SendPacketAsync"/> is the one way the magic packet leaves this PC (given,
/// so tests send nothing). UI thread.
internal sealed class WakeGate
{
    private readonly Dictionary<string, (Guid Id, DateTime Until)> _open = new(); // at most one per PC: a newer press replaces the older grant
    private readonly Func<string, string, string?> _send;
    private readonly Func<DateTime> _now;

    /// <param name="send">Sends the packet for a card address to a network's broadcast: null = sent, else why not.</param>
    /// <param name="now">The clock grants are timed by (UTC).</param>
    public WakeGate(Func<string, string, string?>? send = null, Func<DateTime>? now = null)
    {
        _send = send ?? ((mac, subnet) => MagicPacket.Send(mac, subnet, out var why) ? null : why);
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// A person pressed Wake (or Test wake) for this PC: the grant, good for <paramref name="life"/>.
    public WakeGrant Grant(string pcId, TimeSpan life)
    {
        var grant = new WakeGrant(pcId, Guid.NewGuid());
        _open[pcId] = (grant.Id, _now() + life);
        return grant;
    }

    /// True once for a grant made for <paramref name="pcId"/> that has not expired; it is used up either way, and any other grant is refused.
    public bool TryUse(WakeGrant? grant, string pcId)
    {
        if (grant is null || grant.PcId != pcId || !_open.TryGetValue(pcId, out var open) || open.Id != grant.Id) return false;
        _open.Remove(pcId);
        return _now() < open.Until;
    }

    /// Sends one magic packet, off the UI thread: only right after <see cref="TryUse"/>, or for a paired PC's relay ask its person's press made there.
    public Task<string?> SendPacketAsync(string mac, string subnet) => Task.Run(() => _send(mac, subnet));
}
