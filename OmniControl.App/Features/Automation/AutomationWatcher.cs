using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using OmniControl.App.Shared.Scripts;

namespace OmniControl.App.Features.Automation;

/// Something that happened between two looks: a trigger id (<see cref="AutomationTriggers"/>) and, for a program or device, its name.
public sealed record AutomationEvent(string Kind, string Name = "");

/// Notices what the event triggers wait for: the power source changing, programs starting and closing, USB devices coming and going.
/// <see cref="Poll"/> is called on each tick and returns what changed since the last call; the first call only takes a baseline, so
/// nothing that was already true at start-up counts as an event. Programs are only looked at while <c>watchPrograms</c> is true.
public sealed class AutomationWatcher : IDisposable
{
    private readonly UsbDeviceWatcher _usb = new();
    private readonly ConcurrentQueue<AutomationEvent> _devices = new();
    private bool? _onBattery;
    private HashSet<string>? _programs;
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

    /// <param name="now">The running programs (<see cref="RunningPrograms"/>, read by the caller off the UI thread); null while
    /// <paramref name="watchPrograms"/> is on means the read failed and the last list stays.</param>
    public IReadOnlyList<AutomationEvent> Poll(bool watchPrograms, HashSet<string>? now)
    {
        var events = new List<AutomationEvent>();
        var battery = PowerState.IsOnBattery();
        if (_onBattery is not null && battery is not null && battery != _onBattery)
            events.Add(new AutomationEvent(battery == true ? AutomationTriggers.OnBattery : AutomationTriggers.PluggedIn));
        _onBattery = battery ?? _onBattery;

        if (!watchPrograms)
        {
            _programs = null; // looked at again from scratch when a program rule is switched on
        }
        else if (now is not null)
        {
            if (_programs is not null)
            {
                events.AddRange(now.Except(_programs).Select(p => new AutomationEvent(AutomationTriggers.ProgramStart, p)));
                events.AddRange(_programs.Except(now).Select(p => new AutomationEvent(AutomationTriggers.ProgramClose, p)));
            }
            _programs = now;
        }

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
