using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

/// <summary>
/// Watches an emulator while the user works in it. The screen is read over adb again and again (<see cref="Timing.DeviceSimRecordGap"/>), and
/// every touch that adb reports (<see cref="AdbTouchWatcher"/>) is matched with the screen read just before it: a tap becomes a Tap step
/// naming the element under the finger, a swipe becomes a Swipe step. <see cref="PickAsync"/> instead takes the next touch alone (Re-pick).
/// </summary>
public sealed class SimRecorder : IDisposable
{
    private readonly AdbClient _adb;
    private readonly AdbTouchWatcher _touches;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private SimScreen? _latest;
    private TaskCompletionSource<(TouchGesture Touch, SimScreen? Screen)>? _pick;
    private bool _recording;

    public SimRecorder(AdbClient adb, string adbPath, string serial)
    {
        _adb = adb;
        _touches = new AdbTouchWatcher(adbPath, serial);
        _touches.Touched += OnTouched;
    }

    /// <summary>A touch was turned into a step (raised on a background thread).</summary>
    public event Action<SimStep>? Recorded;

    /// <summary>Starts watching; null when it works, otherwise one plain sentence.</summary>
    public async Task<string?> StartAsync()
    {
        if (await _touches.StartAsync() is { } problem) return problem;
        _ = Task.Run(ReadScreensAsync);
        return null;
    }

    /// <summary>The app that has the screen now, for the recording's first step; empty when it cannot be told.</summary>
    public Task<string> CurrentPackageAsync() => _adb.CurrentPackageAsync();

    /// <summary>From now on every touch becomes a step (<see cref="Recorded"/>).</summary>
    public void BeginRecording() => _recording = true;

    public void EndRecording() => _recording = false;

    /// <summary>Waits for the next touch and gives it with the screen it was made on; null when the wait was cancelled.</summary>
    public async Task<(TouchGesture Touch, SimScreen? Screen)?> PickAsync(CancellationToken cancel)
    {
        var wait = new TaskCompletionSource<(TouchGesture, SimScreen?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _pick = wait;
        using var _ = cancel.Register(() => wait.TrySetCanceled());
        try
        {
            return await wait.Task;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            lock (_gate) if (_pick == wait) _pick = null;
        }
    }

    private async Task ReadScreensAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var dump = await _adb.DumpAsync();
            if (dump.Ok && SimScreen.Parse(dump.Text) is { } screen) lock (_gate) _latest = screen;
            try
            {
                await Task.Delay(Timing.DeviceSimRecordGap, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnTouched(TouchGesture touch)
    {
        SimScreen? screen;
        TaskCompletionSource<(TouchGesture, SimScreen?)>? pick;
        lock (_gate)
        {
            screen = _latest;
            pick = _pick;
            _pick = null;
        }
        if (pick is not null) { pick.TrySetResult((touch, screen)); return; }
        if (!_recording) return;
        Recorded?.Invoke(StepOf(touch, screen));
    }

    /// <summary>The step a touch stands for: a tap on the element under the finger, or a swipe in its direction.</summary>
    public static SimStep StepOf(TouchGesture touch, SimScreen? screen)
    {
        if (!touch.IsTap)
        {
            var swipe = SimSteps.Blank(SimStepKind.Swipe);
            swipe.Direction = touch.Direction;
            return swipe;
        }
        if (screen is not null) return screen.TapAt(touch.ToX, touch.ToY);
        var tap = SimSteps.Blank(SimStepKind.Tap);
        tap.X = touch.ToX;
        tap.Y = touch.ToY;
        return tap;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _touches.Touched -= OnTouched;
        _touches.Dispose();
        lock (_gate) _pick?.TrySetCanceled();
        _stop.Dispose();
    }
}
