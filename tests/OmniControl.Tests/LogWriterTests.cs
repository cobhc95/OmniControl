namespace OmniControl.Tests;

/// The log writer's queue budget, coalescing of repeated lines and roll-over by encoded bytes (no writer thread: lines wait for Flush).
public sealed class LogWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oc-logtest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string File(string name) => Path.Combine(_dir, name);

    private static string Line(string message) => $"12:00:00.000 {message}{Environment.NewLine}";

    [Fact]
    public void Past_the_budget_the_oldest_routine_lines_go_and_errors_stay()
    {
        var log = new LogWriter(queueBudget: Line("x0").Length * sizeof(char) * 3, fileMax: 1_000_000, background: false);
        var routine = File("mouse-log.txt");
        var errors = File(LogFiles.Errors);
        log.Append(errors, Line("error"));
        for (var i = 0; i < 5; i++) log.Append(routine, Line("x" + i));
        log.Flush();
        var text = System.IO.File.ReadAllText(routine);
        Assert.Contains("lines dropped", text);
        Assert.DoesNotContain("x0", text);
        Assert.Contains("x4", text);
        Assert.Contains("error", System.IO.File.ReadAllText(errors));
    }

    [Fact]
    public void Repeated_lines_are_written_once_with_a_count()
    {
        var log = new LogWriter(1_000_000, 1_000_000, background: false);
        var path = File("rules-log.txt");
        for (var i = 0; i < 4; i++) log.Append(path, Line("same"));
        log.Append(path, Line("other"));
        log.Flush();
        var lines = System.IO.File.ReadAllLines(path);
        Assert.Equal(new[] { "12:00:00.000 same", "12:00:00.000 (repeated 3 more times)", "12:00:00.000 other" }, lines);
    }

    [Fact]
    public void Roll_over_counts_encoded_bytes_not_characters()
    {
        var path = File("osd-log.txt");
        var wide = new string('é', 40); // 2 bytes each in UTF-8: 95 bytes a line, 55 characters
        var log = new LogWriter(1_000_000, fileMax: 170, background: false);
        log.Append(path, Line(wide));
        log.Flush();
        log.Append(path, Line(wide + "!"));
        log.Flush();
        Assert.True(System.IO.File.Exists(Path.Combine(_dir, "osd-log.old.txt")));
        Assert.Equal("12:00:00.000 " + wide + "!", System.IO.File.ReadAllLines(path).Single());
    }
}
