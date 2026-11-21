using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.Hotkeys;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// The "Repeated clicking" action: clicks a button again and again at the pointer until it is stopped (toggled, released, a limit, Esc, or the
/// mouse moving). Everything is sent through <see cref="InputSender"/> (marked, so the mouse hook never remaps it, even when the button clicked
/// is the one that started it), off the hook and UI threads. Esc is only read, never hooked, so other apps keep it. Whatever ends it, a
/// button it holds is always let go.
public sealed class RepeatedClicking : IDisposable
{
    private readonly object _gate = new();
    private readonly Random _random = new();
    private CancellationTokenSource? _running;

    /// Raised when it starts or stops (any thread): the master list of states follows.
    public event Action? RunningChanged;

    /// Raised about once a second while running with the badge switched on, and at the end with <c>null</c> (any thread).
    public event Action<long?>? Progress;

    public bool IsRunning
    {
        get { lock (_gate) return _running is not null; }
    }

    /// "Press to toggle": starts it, or stops it when it runs.
    public void Toggle(OptionValues options)
    {
        if (IsRunning) Stop();
        else Start(options);
    }

    /// "While held": starts it (when it is not running) and returns what stops it, or null when the settings say it is a toggle.
    public Action? StartHeld(OptionValues options)
    {
        if (options.Text(MouseOptions.ClickStart) != MouseOptions.StartHeld) return null;
        var started = Start(options);
        return started is null ? () => { } : () => Stop(started);
    }

    public void Stop()
    {
        CancellationTokenSource? running;
        lock (_gate) running = _running;
        Stop(running);
    }

    private static void Stop(CancellationTokenSource? run)
    {
        try
        {
            run?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It ended by itself a moment ago.
        }
    }

    private CancellationTokenSource? Start(OptionValues options)
    {
        var run = new CancellationTokenSource();
        lock (_gate)
        {
            if (_running is not null) return null;
            _running = run;
        }
        RunningChanged?.Invoke();
        Guard.OffUi("Mouse: repeated clicking", () => Loop(options, run));
        return run;
    }

    private void Loop(OptionValues options, CancellationTokenSource run)
    {
        var button = MouseOptions.ButtonNamed(options.Text(MouseOptions.ClickButton));
        var type = options.Text(MouseOptions.ClickType);
        var perSecond = Math.Clamp(options.Int(MouseOptions.ClickSpeed), 1, Timing.ClickSpeedMax);
        var jitter = options.Int(MouseOptions.ClickJitter) / 100.0;
        var stopAfter = options.Text(MouseOptions.ClickStop);
        var maxClicks = stopAfter == MouseOptions.StopClicks ? options.Int(MouseOptions.ClickCount) : long.MaxValue;
        var maxTime = stopAfter == MouseOptions.StopSeconds ? TimeSpan.FromSeconds(options.Int(MouseOptions.ClickSeconds)) : TimeSpan.MaxValue;
        var stopOnMove = options.Bool(MouseOptions.ClickStopOnMove);
        var badge = options.Bool(MouseOptions.ClickBadge);
        var at = options.Text(MouseOptions.ClickWhere) == MouseOptions.AtPoint ? options.Point(MouseOptions.ClickPoint) : null;
        if (at is { } fixedPoint) SystemCommands.SetCursorPos(fixedPoint.X, fixedPoint.Y); // the pointer stays at the point afterwards
        var start = SystemCommands.GetCursorPos();
        var held = false;
        long clicks = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var lastBadge = TimeSpan.Zero - Timing.ClickBadgeEvery;

        bool Done() =>
            run.IsCancellationRequested || clicks >= maxClicks || clock.Elapsed >= maxTime || KeyboardState.EscapeHeld() || (stopOnMove && Moved(start));

        try
        {
            if (type == MouseOptions.TypeHold)
            {
                InputSender.ButtonDown(button);
                held = true;
            }
            var due = TimeSpan.Zero;
            while (!Done())
            {
                if (clock.Elapsed >= due)
                {
                    if (at is { } point && SystemCommands.GetCursorPos() is { } now && now != point && !stopOnMove) SystemCommands.SetCursorPos(point.X, point.Y);
                    if (type == MouseOptions.TypeSingle) InputSender.Click(button);
                    else if (type == MouseOptions.TypeDouble) InputSender.Click(button, twice: true);
                    clicks++;
                    var gap = 1000.0 / perSecond * (1 + (jitter == 0 ? 0 : (_random.NextDouble() * 2 - 1) * jitter));
                    due = clock.Elapsed + TimeSpan.FromMilliseconds(Math.Max(gap, 1000.0 / Timing.ClickSpeedMax));
                }
                if (badge && clock.Elapsed - lastBadge >= Timing.ClickBadgeEvery)
                {
                    lastBadge = clock.Elapsed;
                    Progress?.Invoke(clicks);
                }
                run.Token.WaitHandle.WaitOne(Timing.ClickWatchEvery);
            }
        }
        finally
        {
            if (held) InputSender.ButtonUp(button); // never leave the button down, whatever ended it
            lock (_gate) _running = null;
            run.Dispose();
            if (badge) Progress?.Invoke(null);
            RunningChanged?.Invoke();
        }
    }

    private static bool Moved((int X, int Y)? start) =>
        start is { } from && SystemCommands.GetCursorPos() is { } now
        && (Math.Abs(now.X - from.X) > Timing.ClickMoveTolerance || Math.Abs(now.Y - from.Y) > Timing.ClickMoveTolerance);

    public void Dispose() => Stop();
}
