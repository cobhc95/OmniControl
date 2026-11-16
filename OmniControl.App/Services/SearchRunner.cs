using System.Diagnostics;
using Avalonia.Threading;

namespace OmniControl.App.Services;

/// <summary>
/// Runs the searches of one search box: the matching runs off the UI thread, the newest query wins (an older one still running is
/// cancelled and its result dropped), and a key typed soon after another waits <see cref="Timing.SearchDebounce"/> so a fast typist
/// does not start a search per letter. The first letter after an empty box is searched at once. The result is handed to the UI
/// thread. All calls are made on the UI thread.
/// </summary>
public sealed class SearchRunner<T> : IDisposable
{
    private readonly Action<T> _apply;
    private readonly string? _timingName;
    private readonly Action<Action> _post;
    private readonly TimeSpan _debounce;
    private Pending? _pending;
    private bool _hadQuery;

    // The token sources are not disposed: they hold no timer or wait handle, and a run still in flight may look at its token after it is dropped.
    private sealed record Pending(Func<CancellationToken, T> Compute, CancellationTokenSource Cancel, Stopwatch Clock);

    /// <param name="apply">Shows a result (on the UI thread).</param>
    /// <param name="timingName">Names the list in the timing log (<see cref="UiTiming"/>); null logs nothing.</param>
    /// <param name="post">Runs an action on the UI thread; the dispatcher when null (tests give their own).</param>
    /// <param name="debounce">The wait after a key that follows another; <see cref="Timing.SearchDebounce"/> when null.</param>
    public SearchRunner(Action<T> apply, string? timingName = null, Action<Action>? post = null, TimeSpan? debounce = null)
    {
        _apply = apply;
        _timingName = timingName;
        _post = post ?? (a => Dispatcher.UIThread.Post(a));
        _debounce = debounce ?? Timing.SearchDebounce;
    }

    /// <summary>True while a search was asked for and its result is not shown yet.</summary>
    public bool IsPending => _pending is not null;

    /// <summary>Searches for a new query: <paramref name="compute"/> runs off the UI thread and must only read data that does not change (an index).</summary>
    public void Request(Func<CancellationToken, T> compute)
    {
        Drop();
        var pending = new Pending(compute, new CancellationTokenSource(), Stopwatch.StartNew());
        _pending = pending;
        var wait = _hadQuery ? _debounce : TimeSpan.Zero;
        _hadQuery = true;
        _ = RunAsync(pending, wait);
    }

    /// <summary>The box was emptied (or the list closes): a search still running is dropped, and the next letter is searched at once.</summary>
    public void Cancel()
    {
        _hadQuery = false;
        Drop();
    }

    private void Drop()
    {
        if (_pending is not { } pending) return;
        _pending = null;
        pending.Cancel.Cancel();
    }

    /// <summary>Shows the result of the search still waiting at once, on this thread (a key that acts on the list, such as Enter, must see the newest matches).</summary>
    public void Flush()
    {
        if (_pending is not { } pending) return;
        pending.Cancel.Cancel();
        Show(pending, pending.Compute(CancellationToken.None));
    }

    private async Task RunAsync(Pending pending, TimeSpan wait)
    {
        var token = pending.Cancel.Token;
        try
        {
            if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false);
            var result = await Task.Run(() => pending.Compute(token), token).ConfigureAwait(false);
            _post(() =>
            {
                if (ReferenceEquals(_pending, pending) && !token.IsCancellationRequested) Show(pending, result);
            });
        }
        catch (OperationCanceledException)
        {
            // A newer query took over; its own run shows the result.
        }
    }

    private void Show(Pending pending, T result)
    {
        _pending = null;
        _apply(result);
        if (_timingName is not null) UiTiming.Shown($"Search {_timingName}", pending.Clock);
    }

    public void Dispose() => Cancel();
}
