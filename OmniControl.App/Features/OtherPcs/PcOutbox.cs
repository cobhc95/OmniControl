namespace OmniControl.App.Features.OtherPcs;

/// What one link sends, waiting for its one writer: at most <paramref name="max"/> messages and <paramref name="maxBytes"/> bytes (one message
/// always fits). A newer catalog or state value replaces the waiting one (only the latest matters); everything else keeps its order and is never
/// dropped: when it does not fit, or a <paramref name="write"/> fails (it carries its own deadline), the link is stalled: every waiting message
/// fails and <paramref name="stalled"/> is called once (the owner closes the link, which reconnects normally). The writer runs only while
/// messages wait.
internal sealed class PcOutbox(int max, int maxBytes, Func<byte[], Task> write, Action stalled)
{
    private sealed class Entry(byte[] bytes, string? key)
    {
        public byte[] Bytes = bytes;
        public readonly string? Key = key;
        public readonly TaskCompletionSource<bool> Sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly LinkedList<Entry> _waiting = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _byKey = new();
    private int _bytes;
    private bool _writing, _closed;

    /// Queues a message; the task says whether it was written (false: the link stalled or closed). A replaced value's task follows its newer one.
    public Task<bool> Put(PcMsg message)
    {
        var bytes = message.ToBytes();
        var key = message.LatestKey();
        LinkedListNode<Entry>? node;
        var start = false;
        lock (_waiting)
        {
            if (_closed) return Task.FromResult(false);
            if (key is not null && _byKey.TryGetValue(key, out node))
            {
                _waiting.Remove(node);
                _bytes += bytes.Length - node.Value.Bytes.Length;
                node.Value.Bytes = bytes;
                _waiting.AddLast(node);
            }
            else if (_waiting.Count == 0 || (_waiting.Count < max && _bytes + bytes.Length <= maxBytes))
            {
                node = _waiting.AddLast(new Entry(bytes, key));
                _bytes += bytes.Length;
                if (key is not null) _byKey[key] = node;
            }
            else node = null;
            if (node is not null && !_writing) start = _writing = true;
        }
        if (node is null)
        {
            Stall(); // full: the other PC does not take what is sent
            return Task.FromResult(false);
        }
        if (start) Guard.OffUi("Other PCs: sending", WriteAllAsync); // never a socket write on the UI thread
        return node.Value.Sent.Task;
    }

    /// Fails every waiting message; nothing more is taken.
    public void Close() => CloseQueue();

    private async Task WriteAllAsync()
    {
        Entry? writing = null;
        try
        {
            while ((writing = Take()) is { } entry)
            {
                await write(entry.Bytes).ConfigureAwait(false);
                entry.Sent.TrySetResult(true);
            }
        }
        catch (Exception ex) when (PcWire.IsNetworkError(ex))
        {
            // The write failed or missed its deadline: handled as a stall below.
        }
        finally
        {
            if (writing is not null)
            {
                writing.Sent.TrySetResult(false);
                Stall();
            }
        }
    }

    private Entry? Take()
    {
        lock (_waiting)
        {
            if (_closed || _waiting.First is not { } first)
            {
                _writing = false;
                return null;
            }
            _waiting.RemoveFirst();
            _bytes -= first.Value.Bytes.Length;
            if (first.Value.Key is { } key) _byKey.Remove(key);
            return first.Value;
        }
    }

    private void Stall()
    {
        if (CloseQueue()) stalled();
    }

    private bool CloseQueue()
    {
        List<Entry> dropped;
        lock (_waiting)
        {
            if (_closed) return false;
            _closed = true;
            dropped = _waiting.ToList();
            _waiting.Clear();
            _byKey.Clear();
            _bytes = 0;
        }
        foreach (var entry in dropped) entry.Sent.TrySetResult(false);
        return true;
    }
}
