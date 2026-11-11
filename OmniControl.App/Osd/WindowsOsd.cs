using OmniControl.App.Services;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace OmniControl.App.Osd;

/// <summary>
/// Best-effort suppression of Windows' own volume/brightness pop-up (Windows 11).
/// The pop-up is an Explorer window of class XamlExplorerHostIslandWindow (empty title) holding a
/// DesktopWindowContentBridge "DesktopWindowXamlSource" with an InputSite child. The window only exists once the
/// pop-up has been shown, so if it is missing a mute key pair (net no change) is sent to create it. The window is then
/// minimised, and re-minimised whenever Windows restores it. Found windows are logged to %APPDATA%\OmniControl\osd-log.txt.
/// Approach follows the open-source HideVolumeOSD tool (gpkgpk/HideVolumeOSD, tag v1.3.3.0).
/// </summary>
public sealed class WindowsOsd : IDisposable
{
    private const string HostClass = "XamlExplorerHostIslandWindow";

    private readonly DispatcherTimer _timer = new() { Interval = Timing.WindowsOsdPoll };
    private DateTime _lastCreate = DateTime.MinValue;
    private int _creates;
    private int _logged;

    public WindowsOsd() => _timer.Tick += (_, _) => Tick();

    public bool Hidden
    {
        get => _timer.IsEnabled;
        set
        {
            if (value == _timer.IsEnabled) return;
            if (value) { _creates = 0; _timer.Start(); }
            else { _timer.Stop(); Restore(); }
        }
    }

    /// <summary>Call when Windows is about to show its pop-up (brightness change); lets the window be found again if it was recreated.</summary>
    public void Trigger() => _creates = Math.Min(_creates, 3);

    private void Tick()
    {
        var hosts = FindHosts();
        if (hosts.Count == 0)
        {
            // Windows creates the window lazily: show the pop-up once (mute toggled twice = unchanged) so it can be found.
            if (_creates < 5 && DateTime.UtcNow - _lastCreate > Timing.WindowsOsdRetry)
            {
                _lastCreate = DateTime.UtcNow;
                _creates++;
                Log("no OSD window yet, sending mute pair");
                PressMutePair();
            }
            return;
        }

        foreach (var h in hosts)
            if (!IsIconic(h))
            {
                Log($"minimising OSD window 0x{h.ToInt64():x}");
                ShowWindow(h, 6); // SW_MINIMIZE
            }
    }

    /// <summary>Brings back Windows' own pop-up: restores the windows we minimised, then shows the pop-up once so Windows picks it up again.</summary>
    public void Restore()
    {
        var any = false;
        foreach (var h in FindHosts())
            if (IsIconic(h))
            {
                ShowWindow(h, 9); // SW_RESTORE
                any = true;
            }
        if (any) { Log("restored OSD windows"); PressMutePair(); }
    }

    /// <summary>Mute toggled twice: shows Windows' pop-up without changing the mute state.</summary>
    private static void PressMutePair()
    {
        for (var i = 0; i < 2; i++)
        {
            keybd_event(0xAD, 0, 0, UIntPtr.Zero);
            keybd_event(0xAD, 0, 2, UIntPtr.Zero);
        }
    }

    private static List<IntPtr> FindHosts()
    {
        var list = new List<IntPtr>();
        var host = IntPtr.Zero;
        while ((host = FindWindowEx(IntPtr.Zero, host, HostClass, string.Empty)) != IntPtr.Zero)
        {
            var bridge = FindWindowEx(host, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", "DesktopWindowXamlSource");
            if (bridge == IntPtr.Zero) continue;
            if (FindWindowEx(bridge, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null) == IntPtr.Zero) continue;
            list.Add(host);
        }
        return list;
    }

    private void Log(string line)
    {
        if (_logged++ > 100) return;
        AppDataStore.AppendLog("osd-log.txt", line);
    }

    public void Dispose() => Hidden = false;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
