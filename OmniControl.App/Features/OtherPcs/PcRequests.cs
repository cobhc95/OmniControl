namespace OmniControl.App.Features.OtherPcs;

/// Requests sent to paired PCs: each gets a number, and only the answer with that number from that same PC completes it (UI thread).
internal sealed class PcRequests
{
    private readonly Dictionary<int, (string PcId, TaskCompletionSource<PcMsg> Answer)> _pending = new();
    private int _next;

    /// A new request number (also for a request whose answer is only reported).
    public int Next() => ++_next;

    /// Sends <paramref name="ask"/> and waits at most <paramref name="wait"/> for its answer; null when it could not be sent or none came.
    public async Task<PcMsg?> AskAsync(PcLink link, PcMsg ask, TimeSpan wait, CancellationToken stop = default)
    {
        var rid = Next();
        ask.Rid = rid;
        var answer = new TaskCompletionSource<PcMsg>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[rid] = (link.PeerId, answer);
        try
        {
            if (!await link.SendAsync(ask)) return null;
            return await Task.WhenAny(answer.Task, Task.Delay(wait, stop)) == answer.Task ? answer.Task.Result : null;
        }
        finally
        {
            _pending.Remove(rid);
        }
    }

    /// Completes the request an answer belongs to; false when no request of that PC waits for it.
    public bool Answer(PcLink link, PcMsg message)
    {
        if (message.Rid is not { } rid || !_pending.TryGetValue(rid, out var pending) || pending.PcId != link.PeerId) return false;
        pending.Answer.TrySetResult(message);
        return true;
    }
}

/// A rate limit per paired PC: at most <paramref name="max"/> requests of one kind within each <paramref name="window"/>.
internal sealed class PcLimits(int max, TimeSpan window)
{
    private readonly Dictionary<string, LaunchLimiter> _limits = new();

    public bool TryAcquire(string pcId) => (_limits.TryGetValue(pcId, out var limit) ? limit : _limits[pcId] = new LaunchLimiter(max, window)).TryAcquire();
}
