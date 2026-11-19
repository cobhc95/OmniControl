using System.Collections.Concurrent;

namespace OmniControl.App.Features.Input.Mouse;

/// Runs the effects of mouse modes (sending input, moving the pointer or a window, changing the pointer's look) one at a time, in order,
/// on its own thread. The mouse hook only records what happened and posts here, because Windows delays every mouse input on the
/// system while a low-level hook callback runs, and input sent from inside the callback queues up behind it.
public sealed class InputQueue : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Action<string> _log;
    private readonly Thread _thread;

    /// <param name="log">Where a failed piece of work is reported (the mouse log).</param>
    public InputQueue(Action<string> log)
    {
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "MouseInput" };
        _thread.Start();
    }

    /// Queues <paramref name="work"/>; never blocks. Work posted after <see cref="Dispose"/> is dropped.
    public void Post(Action work)
    {
        try
        {
            _work.Add(work);
        }
        catch (InvalidOperationException)
        {
            // The program is closing and the queue no longer takes work; nothing more should be sent anyway.
        }
    }

    private void Run()
    {
        foreach (var work in _work.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                // One failed Windows call must not stop every later mouse effect; it is logged and the queue carries on.
                _log($"mouse work failed: {ex.Message}");
            }
        }
    }

    /// Finishes the work already queued (so held keys and buttons are let go), then stops the thread.
    public void Dispose()
    {
        _work.CompleteAdding();
        _thread.Join(Timing.MouseQueueDrain);
    }
}
