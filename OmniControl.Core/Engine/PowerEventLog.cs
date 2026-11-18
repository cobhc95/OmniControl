using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using static OmniControl.Core.Native.EventLogNative;

namespace OmniControl.Core.Engine;

/// <summary>One sleep or wake Windows wrote in its System log, with when it happened.</summary>
public sealed record LoggedPowerEvent(DateTime At, PowerSignal Signal);

/// <summary>
/// Reads the sleeps and wakes Windows wrote in its System event log: Kernel-Power 42 (entering sleep; target state 5 = hibernate),
/// Kernel-Power 107 (resumed) and Power-Troubleshooter 1 (woke, with the wake source). Used at start-up to see what happened while OmniControl
/// was not running, and after a wake to label it (sleep or hibernate). Read only; a log Windows will not open gives an empty list and the reason.
/// </summary>
public static class PowerEventLog
{
    private const string Channel = "System", KernelPower = "Microsoft-Windows-Kernel-Power", Troubleshooter = "Microsoft-Windows-Power-Troubleshooter";
    private const int EnteringSleep = 42, Resumed = 107, Woke = 1, HibernateState = 5, Batch = 16;
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    /// <summary>The events after <paramref name="since"/>, oldest first, at most <paramref name="max"/>; the problem when the log could not be read.</summary>
    public static (IReadOnlyList<LoggedPowerEvent> Events, string? Problem) Since(DateTime since, int max)
    {
        var query = $"*[System[TimeCreated[@SystemTime>'{since.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}'] and " +
                    $"((Provider[@Name='{KernelPower}'] and (EventID={EnteringSleep} or EventID={Resumed})) or (Provider[@Name='{Troubleshooter}'] and EventID={Woke}))]]";
        var events = new List<LoggedPowerEvent>();
        var results = EvtQuery(IntPtr.Zero, Channel, query, QueryChannelPath | QueryReverseDirection);
        if (results == IntPtr.Zero) return (events, $"the System log could not be read ({new Win32Exception(Marshal.GetLastWin32Error()).Message})");
        try
        {
            var handles = new IntPtr[Batch];
            while (events.Count < max && EvtNext(results, Batch, handles, CoreTiming.EventLogReadWaitMs, 0, out var returned))
            {
                for (var i = 0; i < returned; i++)
                {
                    try
                    {
                        if (events.Count < max && Render(handles[i]) is { } xml && Parse(xml) is { } logged) events.Add(logged);
                    }
                    finally { EvtClose(handles[i]); }
                }
            }
        }
        finally { EvtClose(results); }
        events.Reverse();
        return (events, null);
    }

    private static string? Render(IntPtr handle)
    {
        EvtRender(IntPtr.Zero, handle, RenderEventXml, 0, IntPtr.Zero, out var used, out _);
        if (Marshal.GetLastWin32Error() != InsufficientBuffer || used <= 0) return null;
        var buffer = Marshal.AllocHGlobal(used);
        try
        {
            return EvtRender(IntPtr.Zero, handle, RenderEventXml, used, buffer, out _, out _) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>One event's XML as a journal event; null when it is not one of ours or cannot be read.</summary>
    internal static LoggedPowerEvent? Parse(string xml)
    {
        XElement root;
        try { root = XElement.Parse(xml); }
        catch (XmlException) { return null; } // a damaged record is skipped, the rest are still read
        var system = root.Element(Ns + "System");
        var provider = system?.Element(Ns + "Provider")?.Attribute("Name")?.Value;
        if (!int.TryParse(system?.Element(Ns + "EventID")?.Value, out var id)
            || !DateTime.TryParse(system?.Element(Ns + "TimeCreated")?.Attribute("SystemTime")?.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
            return null;
        string? Data(string name) => root.Element(Ns + "EventData")?.Elements(Ns + "Data").FirstOrDefault(d => d.Attribute("Name")?.Value == name)?.Value;
        var signal = (provider, id) switch
        {
            (KernelPower, EnteringSleep) when Data("TargetState") == HibernateState.ToString() => new PowerSignal(PowerEventKind.Hibernate, PowerChannel.EventLog, "Kernel-Power 42 (hibernate)"),
            (KernelPower, EnteringSleep) => new PowerSignal(PowerEventKind.Suspend, PowerChannel.EventLog, "Kernel-Power 42 (sleep)"),
            (KernelPower, Resumed) => new PowerSignal(PowerEventKind.Resume, PowerChannel.EventLog, "Kernel-Power 107"),
            (Troubleshooter, Woke) => new PowerSignal(PowerEventKind.Resume, PowerChannel.EventLog, Data("WakeSourceText") is { Length: > 0 } source ? $"Power-Troubleshooter 1, woken by {source}" : "Power-Troubleshooter 1"),
            _ => null,
        };
        return signal is null ? null : new LoggedPowerEvent(at.ToLocalTime(), signal);
    }
}
