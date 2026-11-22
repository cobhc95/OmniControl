using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using OmniControl.App.Shared.Scripts;

namespace OmniControl.App.Features.Automation;

/// Something that happened between two looks: a trigger id (<see cref="AutomationTriggers"/>) and, for a device, its name.
public sealed record AutomationEvent(string Kind, string Name = "");

/// Notices what the event triggers wait for: the power source changing and USB devices coming and going (<see cref="Poll"/>, on each tick).
public sealed class AutomationWatcher : IDisposable
{
    private readonly UsbDeviceWatcher _usb = new();
    private readonly ConcurrentQueue<AutomationEvent> _devices = new();
    private bool? _onBattery;
    private bool _started;

    public AutomationWatcher() => _usb.Changed += OnUsbChanged;

    private void OnUsbChanged(bool connected, string name) =>
        _devices.Enqueue(new AutomationEvent(connected ? AutomationTriggers.DeviceConnect : AutomationTriggers.DeviceDisconnect, name));

    /// Starts watching USB devices (done once a rule wants them, so nothing is watched for no reason).
    public void StartDevices()
    {
        if (_started) return;
        _started = true;
        _usb.Start();
    }

    /// The hardware watch's refresh after Windows announced a device change (worker thread): the USB list is compared, if watched.
    public void LookAtUsb() => _usb.Look();

    /// What changed since the last call: the power source, USB devices. The first call only takes a baseline, so nothing that was already
    /// true at start-up counts as an event.
    public IReadOnlyList<AutomationEvent> Poll()
    {
        var events = new List<AutomationEvent>();
        var battery = PowerState.IsOnBattery();
        if (_onBattery is not null && battery is not null && battery != _onBattery)
            events.Add(new AutomationEvent(battery == true ? AutomationTriggers.OnBattery : AutomationTriggers.PluggedIn));
        _onBattery = battery ?? _onBattery;
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
