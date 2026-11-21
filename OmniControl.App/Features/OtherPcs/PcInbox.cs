namespace OmniControl.App.Features.OtherPcs;

/// What one link read, waiting for the UI thread (where Other PCs' state lives): at most <paramref name="max"/> messages, and while it is full
/// the link stops reading, so TCP slows the other PC instead of the UI queue growing. A newer catalog replaces the waiting one and a newer value
/// of a state replaces its waiting value (moved to the end, after what came before it). At most one <paramref name="post"/> is waiting at a time;
/// it hands every waiting message to <paramref name="handle"/>.
internal sealed class PcInbox(int max, Action<PcMsg> handle, Action<Action> post)
{
    private readonly LinkedList<PcMsg> _waiting = new();
    private readonly Dictionary<string, LinkedListNode<PcMsg>> _byKey = new();
    private readonly SemaphoreSlim _room = new(max, max);
    private bool _posted;

    /// Takes a message from the link's one reader; waits while the inbox is full.
    public async Task PutAsync(PcMsg message, CancellationToken ct)
    {
        var key = KeyOf(message);
        if (!TryReplace(key, message))
        {
            await _room.WaitAsync(ct).ConfigureAwait(false);
            lock (_waiting)
            {
                var node = _waiting.AddLast(message);
                if (key is not null) _byKey[key] = node;
            }
        }
        lock (_waiting)
        {
            if (_posted) return;
            _posted = true;
        }
        post(Drain);
    }

    // A superseded catalog or state value gives its place (and its room) to the newer one.
    private bool TryReplace(string? key, PcMsg message)
    {
        lock (_waiting)
        {
            if (key is null || !_byKey.TryGetValue(key, out var old)) return false;
            _waiting.Remove(old);
            _byKey[key] = _waiting.AddLast(message);
            return true;
        }
    }

    private void Drain()
    {
        List<PcMsg> taken;
        lock (_waiting)
        {
            taken = _waiting.ToList();
            _waiting.Clear();
            _byKey.Clear();
            _posted = false;
        }
        if (taken.Count > 0) _room.Release(taken.Count);
        foreach (var message in taken) handle(message);
    }

    private static string? KeyOf(PcMsg message) => message.T switch
    {
        PcMsgType.Catalog => PcMsgType.Catalog,
        PcMsgType.State when message.Id is { } id => $"{PcMsgType.State}:{id}",
        _ => null,
    };
}
