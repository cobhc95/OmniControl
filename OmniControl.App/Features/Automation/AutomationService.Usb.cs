using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Features.States;

namespace OmniControl.App.Features.Automation;

public sealed partial class AutomationService
{
    private const string UsbPrefix = "pc.usb.";
    private IReadOnlyDictionary<string, string>? _usbDevices;
    private int _usbPickers;
    private bool _lookingAtUsb;

    private void SyncUsb()
    {
        var saved = Settings.Rules.SelectMany(r => r.AllTriggers).Select(MouseAction.Parse)
            .Where(t => t.Id == AutomationTriggers.StateChanges && t.Param.StartsWith(UsbPrefix, StringComparison.Ordinal))
            .Select(t => t.Param).ToHashSet();
        var active = _usbPickers > 0 || (!SessionOff && (saved.Any(_states.IsWatched) ||
            Settings.Rules.Any(r => Needs(r) && IsTrigger(r, AutomationTriggers.DeviceConnect, AutomationTriggers.DeviceDisconnect))));
        _lookingAtUsb = active;
        _watcher.FollowDevices(active);
        if (!active) _usbDevices = null;
        var entries = _states.States.Where(s => s.Id.StartsWith(UsbPrefix, StringComparison.Ordinal)).ToList();
        var offered = _usbDevices is { } devices && _usbPickers > 0 ? devices.Keys.Select(UsbId) : Enumerable.Empty<string>();
        saved.UnionWith(offered);
        _states.Unregister(entries.Where(s => !saved.Contains(s.Id)).Select(s => s.Id));
        var added = saved.Where(id => entries.All(e => e.Id != id)).Select(UsbEntry).ToList();
        _states.Notify(entries.Where(s => saved.Contains(s.Id)).Select(s => s.Id).ToList());
        if (added.Count > 0) _states.Register(added);
    }

    private static string UsbId(string id) => UsbPrefix + Uri.EscapeDataString(id.ToUpperInvariant());

    private StateEntry UsbEntry(string stateId)
    {
        var id = Uri.UnescapeDataString(stateId[UsbPrefix.Length..]);
        var name = _usbDevices?.GetValueOrDefault(id) ?? id;
        return new(stateId, $"{name} connected", AutomationTriggers.Usb, StateKind.OnOff,
            () => _usbDevices is { } devices ? StateValue.Of(devices.ContainsKey(id)) : null, "Windows' USB device list, refreshed after device changes.",
            Keywords: $"usb plug device {id}", Wording: new StateWords(name, "connects", "disconnects", "connects or disconnects"));
    }

    private void OnUsbSeen(IReadOnlyDictionary<string, string> devices) => Dispatcher.UIThread.Post(() => Guard.Run("USB states", () =>
    {
        if (!_lookingAtUsb) return;
        _usbDevices = devices;
        SyncUsb();
        Dispatcher.UIThread.Post(() =>
        {
            if (_usbDevices != devices) return;
            var renamed = devices.Keys.Select(UsbId).Where(id => _states.Find(id) is { } entry && entry.Wording?.Subject == Uri.UnescapeDataString(id[UsbPrefix.Length..]))
                .Select(UsbEntry).ToList();
            if (renamed.Count > 0) _states.Register(renamed);
        });
    }));
}
