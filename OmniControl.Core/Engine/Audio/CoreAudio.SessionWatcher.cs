using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

public static partial class CoreAudio
{
    /// <summary>
    /// Raises <see cref="Created"/> (on a worker thread) the moment any program opens a new audio session on any playback device, so a
    /// program that starts or opens its first stream is seen at once instead of at the next scan. Watches every active playback
    /// device and follows devices being added, removed, enabled or disabled. Windows calls in; nothing is polled.
    /// </summary>
    public sealed class SessionWatcher : IDisposable
    {
        private readonly object _gate = new();
        private readonly SessionCallback _sessionCallback;
        private readonly DeviceCallback _deviceCallback;
        private readonly List<IAudioSessionManager2> _managers = new();
        private IMMDeviceEnumerator? _enumerator;
        private IntPtr _sessionSink, _deviceSink;
        private bool _disposed;

        /// <summary>A program opened a new session somewhere (worker thread; the session itself is read by the next scan).</summary>
        public event Action? Created;

        /// <summary>A playback device was added, removed, enabled or disabled (worker thread): the list of devices is stale.</summary>
        public event Action? DevicesChanged;

        public SessionWatcher()
        {
            // Windows asks not to be called back into from inside its notification, so both events leave its thread first.
            _sessionCallback = new SessionCallback(() => ThreadPool.QueueUserWorkItem(_ => Created?.Invoke()));
            _deviceCallback = new DeviceCallback(() => ThreadPool.QueueUserWorkItem(_ =>
            {
                DevicesChanged?.Invoke();
                Attach();
            }));
        }

        /// <summary>Starts watching on a worker thread (Windows wants the registration from a multithreaded apartment; the first call is slow).</summary>
        public void Start() => Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    _enumerator = NewEnumerator();
                    _sessionSink = Marshal.GetComInterfaceForObject(_sessionCallback, typeof(ISessionNotify));
                    _deviceSink = Marshal.GetComInterfaceForObject(_deviceCallback, typeof(IDeviceNotify));
                    _enumerator.RegisterEndpointNotificationCallback(_deviceSink);
                }
                catch (COMException)
                {
                    return; // no audio service: the mixer's regular scan still finds new programs
                }
            }
            Attach();
        });

        /// <summary>Registers on every active playback device (again after a device change).</summary>
        private void Attach()
        {
            lock (_gate)
            {
                if (_disposed || _enumerator is null) return;
                Detach();
                var iid = typeof(IAudioSessionManager2).GUID;
                foreach (var device in ActiveDevices(_enumerator))
                {
                    try
                    {
                        if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var o) != 0 || o is not IAudioSessionManager2 manager) continue;
                        // Windows sends session notifications only after the manager's session list was asked for once.
                        if (manager.GetSessionEnumerator(out var sessions) == 0) Marshal.ReleaseComObject(sessions);
                        if (manager.RegisterSessionNotification(_sessionSink) == 0) _managers.Add(manager);
                        else Marshal.ReleaseComObject(manager);
                    }
                    catch (COMException)
                    {
                        // The device went away while being opened; the device event that follows attaches again.
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
        }

        /// <summary>Stops watching every device. Call inside the lock.</summary>
        private void Detach()
        {
            foreach (var manager in _managers)
            {
                try
                {
                    manager.UnregisterSessionNotification(_sessionSink);
                }
                catch (COMException)
                {
                    // The device is gone already; its registration went with it.
                }
                Marshal.ReleaseComObject(manager);
            }
            _managers.Clear();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                Detach();
                if (_enumerator is not null)
                {
                    if (_deviceSink != IntPtr.Zero) _enumerator.UnregisterEndpointNotificationCallback(_deviceSink);
                    Marshal.ReleaseComObject(_enumerator);
                    _enumerator = null;
                }
                if (_deviceSink != IntPtr.Zero) Marshal.Release(_deviceSink);
                if (_sessionSink != IntPtr.Zero) Marshal.Release(_sessionSink);
                _deviceSink = _sessionSink = IntPtr.Zero;
            }
        }

        private sealed class SessionCallback : ISessionNotify
        {
            private readonly Action _created;

            public SessionCallback(Action created) => _created = created;

            public int OnSessionCreated(IntPtr session)
            {
                _created();
                return 0;
            }
        }

        private sealed class DeviceCallback : IDeviceNotify
        {
            private readonly Action _devicesChanged;

            public DeviceCallback(Action devicesChanged) => _devicesChanged = devicesChanged;

            public int OnDeviceStateChanged(string id, int state)
            {
                _devicesChanged();
                return 0;
            }

            public int OnDeviceAdded(string id)
            {
                _devicesChanged();
                return 0;
            }

            public int OnDeviceRemoved(string id)
            {
                _devicesChanged();
                return 0;
            }

            public int OnDefaultDeviceChanged(int flow, int role, string? id) => 0;
            public int OnPropertyValueChanged(string id, NotifyKey key) => 0;
        }
    }
}
