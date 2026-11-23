using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using OmniControl.App.Shared.Scripts;

namespace OmniControl.App.Features.Automation;

/// Something that happened between two looks: a trigger id (<see cref="AutomationTriggers"/>) and, for a device, its name.
public sealed record AutomationEvent(string Kind, string Name = "");

/// Notices USB devices coming and going (<see cref="Poll"/>, on each tick).
public sealed class AutomationWatcher : IDisposable
{
    private readonly UsbDeviceWatcher _usb = new();
    private readonly ConcurrentQueue<AutomationEvent> _devices = new();
    public event Action<IReadOnlyDictionary<string, string>> DevicesSeen
    {
        add => _usb.Seen += value;
        remove => _usb.Seen -= value;
    }

    public AutomationWatcher() => _usb.Changed += OnUsbChanged;

    private void OnUsbChanged(bool connected, string name) =>
        _devices.Enqueue(new AutomationEvent(connected ? AutomationTriggers.DeviceConnect : AutomationTriggers.DeviceDisconnect, name));

    /// Starts watching USB devices (done once a rule wants them, so nothing is watched for no reason).
    public void FollowDevices(bool needed)
    {
        if (needed) _usb.Start();
        else
        {
            _usb.Stop();
            _devices.Clear();
        }
    }

    /// The hardware watch's refresh after Windows announced a device change (worker thread): the USB list is compared, if watched.
    public void LookAtUsb() => _usb.Look();

    /// USB devices that came or went since the last call.
    public IReadOnlyList<AutomationEvent> Poll()
    {
        var events = new List<AutomationEvent>();
        while (_devices.TryDequeue(out var device)) events.Add(device);
        return events;
    }

    /// The names of the running programs, as rules write them ("chrome").
    public static HashSet<string> RunningPrograms()
    {
        var names = new HashSet<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    names.Add(ProgramNames.Normalize(process.ProcessName));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // It ended while the list was read, or it belongs to another user: left out.
                }
            }
        }
        return names;
    }

    public void Dispose()
    {
        _usb.Stop();
        _usb.Changed -= OnUsbChanged;
    }
}
