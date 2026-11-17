using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using OmniControl.App.Rules;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Automation;

/// <summary>Something that happened between two looks: a trigger id (<see cref="AutomationTriggers"/>) and, for a program or device, its name.</summary>
public sealed record AutomationEvent(string Kind, string Name = "");

/// <summary>
/// Notices what the event triggers wait for: the power source changing, programs starting and closing, USB devices coming and going.
/// <see cref="Poll"/> is called on each tick and returns what changed since the last call; the first call only takes a baseline, so
/// nothing that was already true at start-up counts as an event. Programs are only looked at while <c>watchPrograms</c> is true.
/// </summary>
public sealed class AutomationWatcher : IDisposable
{
    private readonly UsbDeviceWatcher _usb = new(Timing.AutomationUsbSettle);
    private readonly ConcurrentQueue<AutomationEvent> _devices = new();
    private bool? _onBattery;
    private HashSet<string>? _programs;
    private bool _started;

    public AutomationWatcher()
    {
        _usb.Failed += ex => Guard.Log("Watching USB devices", ex);
        _usb.Changed += (connected, name) =>
            _devices.Enqueue(new AutomationEvent(connected ? AutomationTriggers.DeviceConnect : AutomationTriggers.DeviceDisconnect, name));
    }

    /// <summary>Starts watching USB devices (done once a rule wants them, so nothing is watched for no reason).</summary>
    public void StartDevices()
    {
        if (_started) return;
        _started = true;
        _usb.Start();
    }

    public IReadOnlyList<AutomationEvent> Poll(bool watchPrograms)
    {
        var events = new List<AutomationEvent>();
        var battery = PowerState.IsOnBattery();
        if (_onBattery is not null && battery is not null && battery != _onBattery)
            events.Add(new AutomationEvent(battery == true ? AutomationTriggers.OnBattery : AutomationTriggers.PluggedIn));
        _onBattery = battery ?? _onBattery;

        if (watchPrograms)
        {
            var now = RunningPrograms();
            if (_programs is not null)
            {
                events.AddRange(now.Except(_programs).Select(p => new AutomationEvent(AutomationTriggers.ProgramStart, p)));
                events.AddRange(_programs.Except(now).Select(p => new AutomationEvent(AutomationTriggers.ProgramClose, p)));
            }
            _programs = now;
        }
        else
        {
            _programs = null; // looked at again from scratch when a program rule is switched on
        }

        while (_devices.TryDequeue(out var device)) events.Add(device);
        return events;
    }

    /// <summary>The names of the running programs, as rules write them ("chrome").</summary>
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

    public void Dispose() => _usb.Dispose();
}
