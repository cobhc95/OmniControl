namespace OmniControl.App.Features.Devices;

/// Watches an emulator while the user works in it. The screen is read over adb again and again (<see cref="Timing.DeviceSimRecordGap"/>), and
/// every touch that adb reports (<see cref="AdbTouchWatcher"/>) is matched with the screen read just before it: a tap becomes a Tap step
/// naming the element under the finger, a swipe becomes a Swipe step. <see cref="PickAsync"/> instead takes the next touch alone (Re-pick).
public sealed class SimRecorder : IDisposable
{
    private readonly AdbClient _adb;
    private readonly AdbTouchWatcher _touches;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private SimScreen? _latest, _prior;
    private TouchGesture? _lastTouch;
    private long _lastTouchTick;
    private TaskCompletionSource<(TouchGesture Touch, SimScreen? Screen)>? _pick;
    private bool _recording;
    private Task _touchQueue = Task.CompletedTask;
    private string _appPackage = string.Empty;

    public SimRecorder(AdbClient adb, string adbPath, string serial)
    {
        _adb = adb;
        _touches = new AdbTouchWatcher(adbPath, serial);
        _touches.Touched += OnTouched;
    }

    /// A touch was turned into a step (raised on a background thread).
    public event Action<SimStep>? Recorded;

    /// Starts watching; null when it works, otherwise one plain sentence.
    public async Task<string?> StartAsync()
    {
        if (await _touches.StartAsync() is { } problem) return problem;
        _ = Task.Run(ReadScreensAsync);
        return null;
    }

    /// The app the person worked in, the latest one that was not a home screen; empty when none was seen.
    public string AppPackage { get { lock (_gate) return _appPackage; } }

    /// Takes the app on the screen now as the app worked in, unless it is a home screen.
    public async Task NoteCurrentAppAsync()
    {
        var package = await _adb.CurrentPackageAsync();
        if (!SimSteps.IsLauncher(package)) lock (_gate) _appPackage = package;
    }

    /// From now on every touch becomes a step (<see cref="Recorded"/>).
    public void BeginRecording() => _recording = true;

    public void EndRecording() => _recording = false;

    /// Waits for the next touch and gives it with the screen it was made on; null when the wait was cancelled.
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
            if (dump.Ok && SimScreen.Parse(dump.Text) is { } screen)
                lock (_gate)
                {
                    _prior = _latest;
                    _latest = screen;
                    if (!SimSteps.IsLauncher(screen.AppPackage)) _appPackage = screen.AppPackage;
                }
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
        SimScreen? screen, prior;
        TaskCompletionSource<(TouchGesture, SimScreen?)>? pick;
        lock (_gate)
        {
            // One physical tap can be reported twice: the second report is not a step.
            var now = Environment.TickCount64;
            if (_lastTouch is { } last && touch.IsTap && last.IsTap && now - _lastTouchTick < Timing.DeviceSimSameTouch.TotalMilliseconds
                && Math.Abs(last.ToX - touch.ToX) < Timing.DeviceSimSameTouchPixels && Math.Abs(last.ToY - touch.ToY) < Timing.DeviceSimSameTouchPixels) return;
            _lastTouch = touch;
            _lastTouchTick = now;
            screen = _latest;
            prior = _prior;
            pick = _pick;
            _pick = null;
        }
        if (pick is not null) { pick.TrySetResult((touch, screen)); return; }
        if (!_recording) return;
        // One touch is handled after the one before it, because a tap on a home screen waits for the app to open.
        lock (_gate) _touchQueue = _touchQueue.ContinueWith(_ => RecordAsync(touch, screen, prior), TaskScheduler.Default).Unwrap();
    }

    private async Task RecordAsync(TouchGesture touch, SimScreen? screen, SimScreen? prior)
    {
        if (!touch.IsTap || screen is null)
        {
            Recorded?.Invoke(StepOf(touch, screen));
            return;
        }
        if (SimSteps.IsLauncher(screen.AppPackage) && screen.AppPackage != SimSteps.SystemUiPackage)
        {
            await OpenedFromHomeAsync(screen.WordsAt(touch.ToX, touch.ToY));
            return;
        }
        // The dump may have been taken a moment late or early: the screen after the tap, or the one before it, can still show the element.
        var step = screen.TapAt(touch.ToX, touch.ToY) ?? new[] { prior, await ReadAfterAsync() }.Select(s => s?.TapAt(touch.ToX, touch.ToY)).FirstOrDefault(t => t is not null);
        if (step is null) return; // nothing tappable there (a page container, empty space): no step
        Recorded?.Invoke(step);
    }

    private async Task<SimScreen?> ReadAfterAsync()
    {
        try
        {
            await Task.Delay(Timing.DeviceSimRecordGap, _stop.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        lock (_gate) return _latest;
    }

    /// A tap on a home screen: when an app comes up, that is an Open app step named by the words under its icon (<paramref name="iconWords"/>,
    /// the name the person sees), else by the app's own label; a tap that opens nothing is not a step.
    private async Task OpenedFromHomeAsync(string iconWords)
    {
        try
        {
            await Task.Delay(Timing.DeviceSimLaunchWait, _stop.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        var package = await _adb.CurrentPackageAsync();
        if (SimSteps.IsLauncher(package)) return;
        lock (_gate) _appPackage = package;
        var open = SimSteps.Blank(SimStepKind.OpenApp);
        open.Package = package;
        open.Label = iconWords.Length > 0 ? iconWords : await _adb.AppLabelAsync(package);
        Recorded?.Invoke(open);
    }

    /// The name the app shows under its icon, for the Open app step.
    public Task<string> AppLabelAsync(string package) => _adb.AppLabelAsync(package);

    /// The step a touch stands for: a tap on the element under the finger, or a swipe in its direction.
    public static SimStep StepOf(TouchGesture touch, SimScreen? screen)
    {
        if (!touch.IsTap)
        {
            var swipe = SimSteps.Blank(SimStepKind.Swipe);
            swipe.Direction = touch.Direction;
            return swipe;
        }
        if (screen?.TapAt(touch.ToX, touch.ToY) is { } found) return found;
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
        // _stop is cancelled, not disposed: the screen loop may still read its token once more, and a source without a timer holds nothing.
        lock (_gate) _pick?.TrySetCanceled();
    }
}
