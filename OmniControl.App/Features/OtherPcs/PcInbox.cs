namespace OmniControl.App.Features.OtherPcs;

/// What one link read, waiting for the UI thread (where Other PCs' state lives): at most <paramref name="max"/> messages and
/// <paramref name="maxBytes"/> bytes of them (one message always fits, so a settings import's large answer still passes), and while it is full
/// the link stops reading, so TCP slows the other PC instead of the UI queue growing. A newer catalog replaces the waiting one and a newer value
/// of a state replaces its waiting value (moved to the end, after what came before it). At most one <paramref name="post"/> is waiting at a time;
/// it hands every waiting message to <paramref name="handle"/>.
internal sealed class PcInbox(int max, int maxBytes, Action<PcMsg> handle, Action<Action> post)
{
    private readonly LinkedList<(PcMsg Message, int Bytes)> _waiting = new();
    private readonly Dictionary<string, LinkedListNode<(PcMsg Message, int Bytes)>> _byKey = new();
    private TaskCompletionSource? _room;
    private int _bytes;
    private bool _posted;

    /// Takes a message of <paramref name="bytes"/> bytes from the link's one reader; waits while the inbox is full.
    public async Task PutAsync(PcMsg message, int bytes, CancellationToken ct)
    {
        var key = message.LatestKey();
        while (true)
        {
            Task? full = null;
            var postNow = false;
            lock (_waiting)
            {
                if (key is not null && _byKey.Remove(key, out var old)) // a superseded catalog or state value gives its place (and its room) to the newer one
                {
                    _waiting.Remove(old);
                    _bytes -= old.Value.Bytes;
                    Add(key, message, bytes);
                }
                else if (_waiting.Count == 0 || (_waiting.Count < max && _bytes + bytes <= maxBytes)) Add(key, message, bytes);
                else full = (_room ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                if (full is null && !_posted) postNow = _posted = true;
            }
            if (full is null)
            {
                if (postNow) post(Drain);
                return;
            }
            await full.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private void Add(string? key, PcMsg message, int bytes)
    {
        var node = _waiting.AddLast((message, bytes));
        _bytes += bytes;
        if (key is not null) _byKey[key] = node;
    }

    private void Drain()
    {
        List<PcMsg> taken;
        lock (_waiting)
        {
            taken = _waiting.Select(w => w.Message).ToList();
            _waiting.Clear();
            _byKey.Clear();
            _bytes = 0;
            _posted = false;
            _room?.TrySetResult();
            _room = null;
        }
        foreach (var message in taken) handle(message);
    }
}
