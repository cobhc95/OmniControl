using System.Globalization;
using System.Text.RegularExpressions;

namespace OmniControl.Core.Engine;

/// One touch on the screen, in screen pixels: where the finger went down and where it came up.
public sealed record TouchGesture(int FromX, int FromY, int ToX, int ToY)
{
    public bool IsTap => Math.Abs(ToX - FromX) <= CoreTiming.AdbTapSlopPx && Math.Abs(ToY - FromY) <= CoreTiming.AdbTapSlopPx;

    /// The way a swipe went: up, down, left or right.
    public string Direction => Math.Abs(ToX - FromX) > Math.Abs(ToY - FromY) ? (ToX > FromX ? "right" : "left") : (ToY > FromY ? "down" : "up");
}

/// Reads the lines of <c>adb shell getevent -l</c> and says when a finger came up, and where. The device reports positions in its own range
/// (<c>max</c> of ABS_MT_POSITION_X and Y), which are scaled to the screen's size. One finger only.
public sealed class TouchReader
{
    private static readonly Regex Max = new(@"ABS_MT_POSITION_(?<axis>[XY])\s*:.*?max\s+(?<max>\d+)", RegexOptions.Compiled);

    private readonly int _maxX, _maxY, _width, _height;
    private bool _down, _gotStart;
    private int _x = -1, _y = -1, _fromX, _fromY;

    public TouchReader(int maxX, int maxY, int width, int height)
    {
        _maxX = maxX;
        _maxY = maxY;
        _width = width;
        _height = height;
    }

    /// The ranges in <c>getevent -lp</c> (the first device that reports positions); null when none does.
    public static (int MaxX, int MaxY)? RangesOf(string eventInfo)
    {
        int? x = null, y = null;
        foreach (Match match in Max.Matches(eventInfo))
        {
            var value = int.Parse(match.Groups["max"].Value, CultureInfo.InvariantCulture);
            if (match.Groups["axis"].Value == "X") x ??= value; else y ??= value;
        }
        return x is { } maxX && y is { } maxY ? (maxX, maxY) : null;
    }

    /// Takes one line of <c>getevent -l</c>; the gesture when it ended a touch, else null.
    public TouchGesture? Feed(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = parts.Length > 0 && parts[0].EndsWith(':') ? 1 : 0; // the first word is the device when there is one
        if (parts.Length < at + 3) return null;
        var (type, code, value) = (parts[at], parts[at + 1], parts[at + 2]);
        if (type == "EV_ABS" && code is "ABS_MT_POSITION_X" or "ABS_MT_POSITION_Y" && int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw))
        {
            if (code.EndsWith('X')) _x = Scale(raw, _maxX, _width); else _y = Scale(raw, _maxY, _height);
            if (_down && !_gotStart && _x >= 0 && _y >= 0) { _fromX = _x; _fromY = _y; _gotStart = true; }
            return null;
        }
        if (type == "EV_KEY" && code == "BTN_TOUCH" || type == "EV_ABS" && code == "ABS_MT_TRACKING_ID")
        {
            var up = value is "UP" or "ffffffff";
            if (!up)
            {
                // A touch is announced twice (tracking id and BTN_TOUCH), in either order, after or before its first position.
                if (!_down) { _down = true; _gotStart = false; }
                if (!_gotStart && _x >= 0 && _y >= 0) { _fromX = _x; _fromY = _y; _gotStart = true; }
                return null;
            }
            if (!_down || !_gotStart) { _down = false; return null; }
            _down = false;
            var gesture = new TouchGesture(_fromX, _fromY, _x, _y);
            _x = _y = -1;
            return gesture;
        }
        return null;
    }

    private static int Scale(int raw, int max, int size) => max > 0 && size > 0 ? (int)((long)raw * size / (max + 1)) : raw;
}

/// Streams the touches made on an Android screen (<c>adb shell getevent</c>) so a recording can turn them into steps. It reads the touch
/// device's range and the screen's size first. Dispose it to stop.
public sealed class AdbTouchWatcher : IDisposable
{
    private readonly string _adb;
    private readonly string _serial;
    private NormalRights.Started? _process;

    public AdbTouchWatcher(string adbPath, string serial)
    {
        _adb = adbPath;
        _serial = serial;
    }

    /// A finger came up (raised on a background thread).
    public event Action<TouchGesture>? Touched;

    /// Starts listening; null when it works, otherwise one plain sentence.
    public async Task<string?> StartAsync()
    {
        var info = await AdbClient.RunAsync(_adb, new[] { "-s", _serial, "shell", "getevent -lp" });
        var size = await AdbClient.RunAsync(_adb, new[] { "-s", _serial, "shell", "wm size" });
        if (!info.Ok || !size.Ok) return "adb couldn't read the touch device.";
        if (TouchReader.RangesOf(info.Text) is not { } range || AdbClient.SizeOf(size.Text) is not { } screen) return "The emulator doesn't report touches to adb, so the taps can't be recorded.";
        var reader = new TouchReader(range.MaxX, range.MaxY, screen.Width, screen.Height);

        try
        {
            _process = NormalRights.Start(_adb, new[] { "-s", _serial, "shell", "getevent -l" }, hidden: true, readOutput: true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"adb couldn't start: {ex.Message}";
        }
        var output = _process.Output!;
        _ = _process.Errors!.ReadToEndAsync().ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); // read so a full pipe never blocks it; ends with adb
        _ = Task.Run(async () =>
        {
            try
            {
                while (await output.ReadLineAsync() is { } line)
                    if (reader.Feed(line) is { } gesture) Touched?.Invoke(gesture);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The stream ended because the watcher was stopped (or adb went away): there are no more touches to read.
            }
        });
        return null;
    }

    public void Dispose()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try { process.Process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* it ended already */ }
        process.Dispose();
    }
}
