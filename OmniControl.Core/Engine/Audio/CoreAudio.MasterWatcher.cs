using OmniControl.Core.Models;
using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine;

public static partial class CoreAudio
{
    /// <summary>
    /// Raises <see cref="Changed"/> (percent, muted; on a Windows audio thread) when the master volume or mute of the default playback
    /// device changes, from anywhere. Windows calls in when it happens, so nothing is polled: a poll asked Windows twice per 16 ms and
    /// left a trail of COM objects for the garbage collector. When the default device changes, the watch moves to the new device.
    /// </summary>
    public sealed class MasterWatcher : IDisposable
    {
        private readonly object _gate = new();
        private readonly VolumeCallback _volumeCallback;
        private readonly DeviceCallback _deviceCallback;
        private IMMDeviceEnumerator? _enumerator;
        private IAudioEndpointVolume? _endpoint;
        private IntPtr _volumeSink, _deviceSink;
        private int _last = -1;
        private bool _lastMuted, _disposed;

        public event Action<int, bool>? Changed;

        /// <summary>Which device to watch (null result = the default playback device); asked at every attach, on a worker thread.</summary>
        public Func<string?>? Device { get; set; }

        /// <summary>Looks again at which device to watch (the answer of <see cref="Device"/> changed); a level that differs is not reported.</summary>
        public void Retarget() => ThreadPool.QueueUserWorkItem(_ => Attach(report: false));

        public MasterWatcher()
        {
            _volumeCallback = new VolumeCallback(Notify);
            // Windows asks not to be called back from inside its own notification, so the move to a new device runs on a worker.
            _deviceCallback = new DeviceCallback(() => ThreadPool.QueueUserWorkItem(_ => Attach(report: true)));
        }

        /// <summary>Starts watching on a worker thread (the first Core Audio call is slow).</summary>
        public void Start() => Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    _enumerator = NewEnumerator();
                    _deviceSink = Marshal.GetComInterfaceForObject(_deviceCallback, typeof(IDeviceNotify));
                    _enumerator.RegisterEndpointNotificationCallback(_deviceSink);
                }
                catch (COMException)
                {
                    return; // Core Audio unavailable (audio service stopped): there is simply no volume pop-up for outside changes
                }
            }
            Attach(report: false);
        });

        /// <summary>Watches the current default device; reports its level when it differs from the last one seen and <paramref name="report"/> is set.</summary>
        private void Attach(bool report)
        {
            int percent;
            bool muted;
            lock (_gate)
            {
                if (_disposed || _enumerator is null) return;
                Detach();
                try
                {
                    IMMDevice device;
                    if (Device?.Invoke() is { } id) { if (_enumerator.GetDevice(id, out device) != 0) return; } // unplugged now
                    else if (_enumerator.GetDefaultAudioEndpoint(Render, Multimedia, out device) != 0) return; // no playback device now
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    var activated = device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var o);
                    Marshal.ReleaseComObject(device);
                    if (activated != 0 || o is not IAudioEndpointVolume endpoint) return;
                    _endpoint = endpoint;
                    _volumeSink = Marshal.GetComInterfaceForObject(_volumeCallback, typeof(IVolumeNotify));
                    endpoint.RegisterControlChangeNotify(_volumeSink);
                    if (endpoint.GetMasterVolumeLevelScalar(out var level) != 0 || endpoint.GetMute(out muted) != 0) return;
                    percent = Percent.FromScalar(level);
                }
                catch (COMException)
                {
                    return; // the device went away while being opened; the next default-device change attaches again
                }
                if (!Remember(percent, muted) || !report) return;
            }
            Changed?.Invoke(percent, muted);
        }

        /// <summary>True when the level differs from the last one seen (also the first time). Call inside the lock.</summary>
        private bool Remember(int percent, bool muted)
        {
            var changed = percent != _last || muted != _lastMuted;
            (_last, _lastMuted) = (percent, muted);
            return changed;
        }

        private void Notify(int percent, bool muted)
        {
            lock (_gate)
            {
                // Channel (balance) changes are announced too; only a new level or mute state is news.
                if (_disposed || !Remember(percent, muted)) return;
            }
            Changed?.Invoke(percent, muted);
        }

        /// <summary>Stops watching the current device. Call inside the lock.</summary>
        private void Detach()
        {
            if (_endpoint is not null)
            {
                if (_volumeSink != IntPtr.Zero) _endpoint.UnregisterControlChangeNotify(_volumeSink);
                Marshal.ReleaseComObject(_endpoint);
                _endpoint = null;
            }
            if (_volumeSink != IntPtr.Zero)
            {
                Marshal.Release(_volumeSink);
                _volumeSink = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                Detach();
                if (_enumerator is null) return;
                if (_deviceSink != IntPtr.Zero)
                {
                    _enumerator.UnregisterEndpointNotificationCallback(_deviceSink);
                    Marshal.Release(_deviceSink);
                    _deviceSink = IntPtr.Zero;
                }
                Marshal.ReleaseComObject(_enumerator);
                _enumerator = null;
            }
        }

        /// <summary>IAudioEndpointVolumeCallback. Public only because Windows can only call interfaces that are visible to COM.</summary>
        [ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IVolumeNotify
        {
            [PreserveSig] int OnNotify(IntPtr data);
        }

        private sealed class VolumeCallback : IVolumeNotify
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA: event context (GUID, 16 bytes), muted (BOOL), master level (float), channels...
            private const int MutedOffset = 16, LevelOffset = 20;
            private readonly Action<int, bool> _changed;

            public VolumeCallback(Action<int, bool> changed) => _changed = changed;

            public int OnNotify(IntPtr data)
            {
                if (data == IntPtr.Zero) return 0;
                var muted = Marshal.ReadInt32(data, MutedOffset) != 0;
                var level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, LevelOffset));
                _changed(Percent.FromScalar(level), muted);
                return 0;
            }
        }

        private sealed class DeviceCallback : IDeviceNotify
        {
            private readonly Action _defaultChanged;

            public DeviceCallback(Action defaultChanged) => _defaultChanged = defaultChanged;

            public int OnDefaultDeviceChanged(int flow, int role, string? id)
            {
                if (flow == Render && role == Multimedia) _defaultChanged();
                return 0;
            }

            public int OnDeviceStateChanged(string id, int state) => 0;
            public int OnDeviceAdded(string id) => 0;
            public int OnDeviceRemoved(string id) => 0;
            public int OnPropertyValueChanged(string id, NotifyKey key) => 0;
        }
    }
}
