using System.Runtime.ExceptionServices;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// Runs a slow sink (the GIF's palette and compression) on a worker thread of its own so copying the screen and encoding overlap. The
/// queue holds one frame: while the worker is busy and a frame already waits, a new frame is dropped, so the one waiting simply lasts
/// longer (its end is the next frame that gets through, as with a repeated frame) and nothing ever piles up. A failure on the worker
/// is thrown again on the recording thread at the next frame or at <see cref="Finish"/>. The worker ends on <see cref="Finish"/> or
/// <see cref="Dispose"/>; the inner sink is disposed with it.
/// </summary>
internal sealed class QueuedVideoSink : IVideoSink
{
    private readonly IVideoSink _inner;
    private readonly int _row, _height;
    private readonly object _gate = new();
    private readonly Thread _worker;
    private byte[] _waiting, _working;
    private long _waitingAt;
    private bool _full, _closing;
    private long _length, _done;
    private volatile ExceptionDispatchInfo? _failure;

    public QueuedVideoSink(IVideoSink inner, int width, int height, string name)
    {
        _inner = inner;
        _row = width * ScreenImage.BytesPerPixel;
        _height = height;
        _waiting = new byte[_row * height];
        _working = new byte[_row * height];
        _worker = new Thread(Work) { IsBackground = true, Name = name };
        _worker.Start();
    }

    public long Length => Interlocked.Read(ref _length);

    public long FramesDone => Interlocked.Read(ref _done);

    public void WriteFrame(ReadOnlySpan<byte> bgra, int stride, long at)
    {
        _failure?.Throw();
        lock (_gate)
        {
            if (_full) return; // the frame waiting lasts longer instead
            for (var y = 0; y < _height; y++) bgra.Slice(y * stride, _row).CopyTo(_waiting.AsSpan(y * _row, _row));
            _waitingAt = at;
            _full = true;
            Monitor.Pulse(_gate);
        }
    }

    public void Finish(long end)
    {
        Close();
        _failure?.Throw();
        _inner.Finish(end);
    }

    public void Dispose()
    {
        Close();
        _inner.Dispose();
    }

    /// <summary>Lets the worker write the frame still waiting, then ends it.</summary>
    private void Close()
    {
        lock (_gate)
        {
            _closing = true;
            Monitor.Pulse(_gate);
        }
        _worker.Join();
    }

    private void Work()
    {
        while (true)
        {
            long at;
            lock (_gate)
            {
                while (!_full && !_closing) Monitor.Wait(_gate);
                if (!_full) return;
                (_waiting, _working) = (_working, _waiting);
                at = _waitingAt;
                _full = false;
            }
            try
            {
                _inner.WriteFrame(_working, _row, at);
                Interlocked.Exchange(ref _length, _inner.Length);
                Interlocked.Increment(ref _done);
            }
            catch (Exception ex)
            {
                _failure = ExceptionDispatchInfo.Capture(ex); // handed to the recording thread, which ends the recording and reports it
                lock (_gate) _closing = true;
                return;
            }
        }
    }
}
