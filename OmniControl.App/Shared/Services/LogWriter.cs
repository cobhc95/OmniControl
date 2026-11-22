using System.Collections.Concurrent;
using System.Text;

namespace OmniControl.App.Shared.Services;

/// THE writer of the diagnostics logs (<see cref="AppDataStore.AppendLog"/>): a line is queued and the caller goes on at once (no lock,
/// no wait); one background thread appends the queued lines in bounded batches and chunks, file by file. Waiting lines hold at most
/// <see cref="Timing.LogQueueMaxBytes"/>: past it the oldest routine lines are dropped and one "N lines dropped" line says so (errors and
/// hangs are never dropped). The same line repeated back to back is written once with a "(repeated N more times)" line. A log that
/// would pass <see cref="Timing.LogMaxBytes"/> (encoded bytes) is rolled over: it becomes "name.old.txt" (replacing the one before) and
/// a new file starts. <see cref="Flush"/> writes what is queued on the calling thread (at exit, and before the process ends on an
/// unhandled exception). <see cref="Default"/> is process-wide on purpose: the logs are one set of files that every part of the app
/// writes to (an allowed exception to "no static state", MAP.md).
internal sealed class LogWriter
{
    public static readonly LogWriter Default = new(Timing.LogQueueMaxBytes, Timing.LogMaxBytes, background: true);

    private readonly record struct Item(string Path, string Line);

    private sealed class Repeat
    {
        public string Message = "";
        public string Stamp = "";
        public int Count;
    }

    private readonly ConcurrentQueue<Item> _kept = new(), _routine = new();
    private readonly ConcurrentDictionary<string, int> _dropped = new(StringComparer.OrdinalIgnoreCase);
    private readonly long _budget, _fileMax;
    private long _bytes;
    private int _signalled;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly Lazy<Thread>? _worker;
    private readonly object _writeGate = new();
    private readonly Dictionary<string, Repeat> _last = new(StringComparer.OrdinalIgnoreCase); // one per log file, under the write gate

    /// <param name="background">Starts its writer thread at the first line; without one, lines wait for <see cref="Flush"/> (tests).</param>
    internal LogWriter(long queueBudget, long fileMax, bool background)
    {
        _budget = queueBudget;
        _fileMax = fileMax;
        if (background)
            _worker = new(() =>
            {
                var thread = new Thread(Run) { IsBackground = true, Name = "Log writer", Priority = ThreadPriority.BelowNormal };
                thread.Start();
                return thread;
            });
    }

    /// Queues one finished line (with its time and line break) for the file at <paramref name="path"/>. Any thread; never throws or waits.
    public void Append(string path, string line)
    {
        (IsKept(path) ? _kept : _routine).Enqueue(new(path, line));
        Interlocked.Add(ref _bytes, SizeOf(line));
        while (Interlocked.Read(ref _bytes) > _budget && _routine.TryDequeue(out var old))
        {
            Interlocked.Add(ref _bytes, -SizeOf(old.Line));
            _dropped.AddOrUpdate(old.Path, 1, (_, n) => n + 1);
        }
        if (_worker is null || Interlocked.Exchange(ref _signalled, 1) != 0) return; // the writer was already told
        _ = _worker.Value;
        _wake.Release();
    }

    /// Writes everything queued now, on this thread.
    public void Flush() => Drain();

    private static bool IsKept(string path) =>
        Path.GetFileName(path) is var name && (name.Equals(LogFiles.Errors, StringComparison.OrdinalIgnoreCase) || name.Equals(LogFiles.Hang, StringComparison.OrdinalIgnoreCase));

    private static long SizeOf(string line) => (long)line.Length * sizeof(char);

    private void Run()
    {
        while (true)
        {
            _wake.Wait();
            Volatile.Write(ref _signalled, 0); // before draining: a line queued from now on wakes the writer again
            Drain();
        }
    }

    private void Drain()
    {
        lock (_writeGate)
        {
            var text = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in _dropped.Keys)
                if (_dropped.TryRemove(path, out var n))
                    Add(text, path, $"{DateTime.Now:HH:mm:ss.fff} {n} log lines dropped: they came faster than they could be written.{Environment.NewLine}");
            int taken;
            do
            {
                taken = 0;
                while (taken < Timing.LogBatchLines && (_kept.TryDequeue(out var item) || _routine.TryDequeue(out item)))
                {
                    Interlocked.Add(ref _bytes, -SizeOf(item.Line));
                    Coalesce(text, item);
                    taken++;
                }
                if (taken < Timing.LogBatchLines) foreach (var (path, repeat) in _last) EndRepeat(text, path, repeat);
                WriteAll(text);
            }
            while (taken == Timing.LogBatchLines);
        }
    }

    private void WriteAll(Dictionary<string, StringBuilder> text)
    {
        foreach (var (path, builder) in text)
        {
            if (builder.Length == 0) continue;
            Write(path, builder.ToString());
            builder.Clear();
        }
    }

    /// A line whose message matches the one just written to its file only counts; the count is written when another line comes or the drain ends.
    private void Coalesce(Dictionary<string, StringBuilder> text, Item item)
    {
        var space = item.Line.IndexOf(' ');
        var (stamp, message) = space < 0 ? ("", item.Line) : (item.Line[..space], item.Line[(space + 1)..]);
        if (!_last.TryGetValue(item.Path, out var repeat)) _last[item.Path] = repeat = new Repeat();
        else if (repeat.Message == message) { repeat.Count++; repeat.Stamp = stamp; return; }
        EndRepeat(text, item.Path, repeat);
        repeat.Message = message;
        Add(text, item.Path, item.Line);
    }

    private void EndRepeat(Dictionary<string, StringBuilder> text, string path, Repeat repeat)
    {
        if (repeat.Count == 0) return;
        Add(text, path, $"{repeat.Stamp} (repeated {repeat.Count} more times){Environment.NewLine}");
        repeat.Count = 0;
    }

    /// Adds to the file's waiting text, writing it out whenever it reaches <see cref="Timing.LogChunkChars"/>.
    private void Add(Dictionary<string, StringBuilder> text, string path, string line)
    {
        if (!text.TryGetValue(path, out var builder)) text[path] = builder = new StringBuilder();
        builder.Append(line);
        if (builder.Length < Timing.LogChunkChars) return;
        Write(path, builder.ToString());
        builder.Clear();
    }

    private void Write(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new FileInfo(path);
            if (file.Exists && file.Length + Encoding.UTF8.GetByteCount(text) > _fileMax)
                File.Move(path, Path.ChangeExtension(path, null) + ".old" + Path.GetExtension(path), overwrite: true);
            File.AppendAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics are optional: a locked or read-only log loses these lines, never the app.
        }
    }
}
