using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// One recording's diagnostic lines, on for every recording: each line goes at once to the app's log (through the sink the caller gives)
/// with the time since the recording was asked for and the thread, and is kept so "Copy details" can hand the whole recording's lines on.
/// A step (<see cref="Step{T}"/>) is logged before it starts and again with how long it took or its HRESULT, so a call that never returns
/// shows as the last line. Thread-safe; lines are written one at a time.
/// </summary>
public sealed class RecordingLog
{
    /// <summary>Lines kept for "Copy details"; a very long recording keeps its first ones (the start is what matters).</summary>
    private const int MaxKept = 600;

    private readonly Action<string> _sink;
    private readonly Stopwatch _since = Stopwatch.StartNew();
    private readonly object _gate = new(), _write = new();
    private readonly List<string> _lines = new();

    public RecordingLog(Action<string> sink) => _sink = sink;

    public void Line(string text)
    {
        var line = $"Recording +{_since.Elapsed.TotalSeconds:0.000}s [{Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString()}] {text}";
        lock (_gate)
        {
            if (_lines.Count < MaxKept) _lines.Add(line);
        }
        lock (_write) _sink(line); // one writer at a time; reading Text never waits for the file
    }

    /// <summary>Every line so far, for "Copy details".</summary>
    public string Text
    {
        get
        {
            lock (_gate) return string.Join(Environment.NewLine, _lines);
        }
    }

    /// <summary>Runs one step, logged before it starts and after with its time, or with its HRESULT when it throws (which goes on).</summary>
    public T Step<T>(string what, Func<T> step)
    {
        Line($"{what}...");
        var watch = Stopwatch.StartNew();
        try
        {
            var result = step();
            Line($"{what}: done in {watch.ElapsedMilliseconds} ms");
            return result;
        }
        catch (Exception ex)
        {
            Line($"{what}: failed in {watch.ElapsedMilliseconds} ms, {Describe(ex)}");
            throw;
        }
    }

    public void Step(string what, Action step) => Step(what, () =>
    {
        step();
        return 0;
    });

    /// <summary>An exception in one line: its HRESULT for a Windows failure, its type and message.</summary>
    public static string Describe(Exception ex) =>
        ex is COMException or UnauthorizedAccessException ? $"HRESULT 0x{ex.HResult:X8} ({ex.Message})" : $"{ex.GetType().Name}: {ex.Message}";
}
