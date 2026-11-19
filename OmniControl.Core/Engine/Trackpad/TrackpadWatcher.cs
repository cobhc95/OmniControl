using OmniControl.Core.Engine.Mouse;
using static OmniControl.Core.Engine.Trackpad.RawInputNative;
using static OmniControl.Core.Native.WindowNative;

namespace OmniControl.Core.Engine.Trackpad;

/// Reads every precision touchpad's own reports (Raw Input, also while OmniControl is in the background) on its own thread with a
/// hidden message-only window, and raises <see cref="Recognised"/> for each gesture the <see cref="GestureRecogniser"/> finds.
/// Raw input is a copy: Windows still does what it does with the touch. Per report nothing is allocated; the work is a few HID
/// field reads. Dispose stops the registration, closes the window and frees every touchpad's data.
public sealed unsafe class TrackpadWatcher : IDisposable
{
    private const string ClassName = "OmniControlTrackpadWatcher";
    private const int GidcArrival = 1, GidcRemoval = 2;
    private const int StartBufferSize = 1024;

    private readonly WndProc _proc; // kept in a field so the garbage collector cannot free the callback while Windows holds it
    private readonly GestureRecogniser _recogniser;
    private readonly Dictionary<IntPtr, TouchpadDevice> _devices = new(); // watcher thread only
    private TouchpadDevice? _only;  // the touchpad used for reports that come without a device handle (Windows filters PTP input)
    private IntPtr _buffer;
    private uint _bufferSize;
    private IntPtr _window;
    private Thread? _thread;

    /// A gesture was recognised (raised on the watcher's thread; handlers must return at once).
    public event Action<MouseButton>? Recognised;

    /// A touchpad was plugged in or removed (raised on the watcher's thread).
    public event Action? DevicesChanged;

    /// Where the watcher writes what it did (started, touchpads found, errors). Never called per report.
    public Action<string>? Log { get; set; }

    /// <param name="cooldown">Least time between two recognised gestures.</param>
    public TrackpadWatcher(TimeSpan cooldown)
    {
        _proc = WindowProc;
        _recogniser = new GestureRecogniser(cooldown);
    }

    /// True when Windows lists a precision touchpad now.
    public static bool IsPresent() => TouchpadDevice.ListHandles().Count > 0;

    /// Starts reading on a new thread. Returns null when running, or one plain sentence saying why it could not start.
    public string? Start()
    {
        string? problem = null;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Loop(ready, p => problem = p)) { IsBackground = true, Name = "TrackpadWatcher", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait();
        return problem;
    }

    /// Stops reading: the window unregisters and closes on its own thread, which then frees everything and ends. Waits for that (briefly),
    /// so a watcher started right after this one is never unregistered by this one's late clean-up (registration is per process).
    public void Dispose()
    {
        var window = _window;
        if (window != IntPtr.Zero) PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(CoreTiming.TrackpadStopWaitMs);
        _thread = null;
    }

    private void Loop(ManualResetEventSlim ready, Action<string?> started)
    {
        var instance = GetModuleHandle(null);
        var problem = Open(instance);
        started(problem);
        ready.Set(); // after this Start returns and disposes the event: it is not touched again
        if (problem is null)
        {
            Log?.Invoke($"touchpad watcher started, {_devices.Count} precision touchpad(s)");
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) DispatchMessage(ref msg);
        }
        Close(instance);
        Log?.Invoke(problem is null ? "touchpad watcher stopped" : $"touchpad watcher not started: {problem}");
    }

    /// Creates the window, reads the touchpads' layouts and registers for their reports; the reason when that fails.
    private string? Open(IntPtr instance)
    {
        var wc = new WndClassEx { Size = (uint)Marshal.SizeOf<WndClassEx>(), WndProc = Marshal.GetFunctionPointerForDelegate(_proc), Instance = instance, ClassName = ClassName };
        const int classExists = 1410;
        if (RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() is var error && error != classExists)
            return $"Windows refused the touchpad listener (error {error}).";
        _window = CreateWindowEx(0, ClassName, null, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window == IntPtr.Zero) return $"Windows refused the touchpad listener (error {Marshal.GetLastWin32Error()}).";

        _bufferSize = StartBufferSize;
        _buffer = Marshal.AllocHGlobal((int)_bufferSize);
        foreach (var handle in TouchpadDevice.ListHandles()) Add(handle);

        var register = new[] { new RawInputDevice { UsagePage = DigitizerPage, Usage = TouchpadUsage, Flags = RidevInputSink | RidevDevNotify, Target = _window } };
        return RegisterRawInputDevices(register, 1, (uint)sizeof(RawInputDevice))
            ? null
            : $"Windows refused to pass touchpad input to OmniControl (error {Marshal.GetLastWin32Error()}).";
    }

    private void Close(IntPtr instance)
    {
        if (_window != IntPtr.Zero) DestroyWindow(_window); // no-op when WM_CLOSE already destroyed it
        _window = IntPtr.Zero;
        UnregisterClass(ClassName, instance);
        foreach (var device in _devices.Values) device.Dispose();
        _devices.Clear();
        _only = null;
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case WmInput:
                    OnInput(lParam);
                    break; // Windows asks for DefWindowProc after WM_INPUT so it can clean up
                case WmInputDeviceChange:
                    OnDeviceChange((int)wParam, lParam);
                    return IntPtr.Zero;
                case WmClose:
                    Unregister();
                    DestroyWindow(hwnd);
                    _window = IntPtr.Zero;
                    return IntPtr.Zero;
                case WmDestroy:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            // An exception escaping a window procedure would take the whole app down; the report is dropped and the error logged.
            Log?.Invoke($"touchpad watcher failed on message {message}: {ex}");
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void Unregister()
    {
        var remove = new[] { new RawInputDevice { UsagePage = DigitizerPage, Usage = TouchpadUsage, Flags = RidevRemove, Target = IntPtr.Zero } };
        RegisterRawInputDevices(remove, 1, (uint)sizeof(RawInputDevice));
    }

    private void OnInput(IntPtr rawInput)
    {
        var size = _bufferSize;
        if (GetRawInputData(rawInput, RidInput, (void*)_buffer, ref size, (uint)HeaderSize) == uint.MaxValue)
        {
            // The buffer is too small (a touchpad with long reports): grow it once and read again.
            size = 0;
            GetRawInputData(rawInput, RidInput, null, ref size, (uint)HeaderSize);
            if (size <= _bufferSize) return;
            Marshal.FreeHGlobal(_buffer);
            (_buffer, _bufferSize) = (Marshal.AllocHGlobal((int)size), size);
            if (GetRawInputData(rawInput, RidInput, (void*)_buffer, ref size, (uint)HeaderSize) == uint.MaxValue) return;
        }

        var bytes = (byte*)_buffer;
        var header = (RawInputHeader*)bytes;
        if (header->Type != RimTypeHid || DeviceOf(header->Device) is not { } device) return;
        var reportSize = *(uint*)(bytes + HeaderSize);
        var reports = *(uint*)(bytes + HeaderSize + 4);
        for (var i = 0u; i < reports; i++)
        {
            if (!device.Read(bytes + ReportsOffset + i * reportSize, reportSize, out var frame)) continue;
            if (_recogniser.Feed(frame, Environment.TickCount64) is { } gesture) Recognised?.Invoke(gesture.Button);
        }
    }

    private TouchpadDevice? DeviceOf(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return _only;
        if (_devices.TryGetValue(handle, out var device)) return device;
        return Add(handle); // a touchpad not listed at the start (rare); read once, then kept
    }

    private TouchpadDevice? Add(IntPtr handle)
    {
        if (_devices.ContainsKey(handle) || TouchpadDevice.Open(handle) is not { } device) return null;
        _devices[handle] = device;
        _only = _devices.Values.First();
        return device;
    }

    private void OnDeviceChange(int change, IntPtr handle)
    {
        if (change == GidcArrival && Add(handle) is not null) Log?.Invoke("touchpad plugged in");
        else if (change == GidcRemoval && _devices.Remove(handle, out var gone))
        {
            gone.Dispose();
            _only = _devices.Values.FirstOrDefault();
            Log?.Invoke("touchpad removed");
        }
        else return;
        DevicesChanged?.Invoke();
    }
}
