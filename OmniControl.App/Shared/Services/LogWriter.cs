using System.Collections.Concurrent;

namespace OmniControl.App.Shared.Services;

/// THE writer of the diagnostics logs (<see cref="AppDataStore.AppendLog"/>): a line is queued and the caller goes on at once; one
/// background thread appends the queued lines, file by file. A log that would pass <see cref="Timing.LogMaxBytes"/> is rolled over: it
/// becomes "name.old.txt" (replacing the one before) and a new file starts, so no log grows without end. <see cref="Flush"/> writes what
/// is queued on the calling thread (at exit, and before the process ends on an unhandled exception). Process-wide on purpose: the logs are
/// one set of files that every part of the app writes to (an allowed exception to "no static state", MAP.md).
internal static class LogWriter
{
    private static readonly ConcurrentQueue<(string Path, string Line)> Pending = new();
    private static readonly SemaphoreSlim Wake = new(0);
    private static readonly object WriteGate = new();
    private static readonly Lazy<Thread> Worker = new(() =>
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Log writer", Priority = ThreadPriority.BelowNormal };
        thread.Start();
        return thread;
    });

    /// Queues one finished line (with its time and line break) for the file at <paramref name="path"/>. Any thread; never throws.
    public static void Append(string path, string line)
    {
        Pending.Enqueue((path, line));
        _ = Worker.Value;
        Wake.Release();
    }

    /// Writes everything queued now, on this thread.
    public static void Flush() => Drain();

    private static void Run()
    {
        while (true)
        {
            Wake.Wait();
            Drain();
        }
    }

    private static void Drain()
    {
        lock (WriteGate)
        {
            var batch = new List<(string Path, string Line)>();
            while (Pending.TryDequeue(out var item)) batch.Add(item);
            foreach (var file in batch.GroupBy(b => b.Path)) Write(file.Key, string.Concat(file.Select(b => b.Line)));
        }
    }

    private static void Write(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new FileInfo(path);
            if (file.Exists && file.Length + text.Length > Timing.LogMaxBytes)
                File.Move(path, Path.ChangeExtension(path, null) + ".old" + Path.GetExtension(path), overwrite: true);
            File.AppendAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics are optional: a locked or read-only log loses these lines, never the app.
        }
    }
}
