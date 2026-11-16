using OmniControl.App.Hotkeys;

namespace OmniControl.App.Devices;

// The master actions of a device (docs/mockups/device-multiple-methods.html), all in HotkeyRegistry: "<name>: On / Off / Toggle" try the
// methods in order; "<name>: On / Off / Toggle via <method>" use that one method. The order ids keep the device's id (shortcuts made before
// devices had several methods still fit); the "via" ids carry the method's id, so they follow a method merged into another device.
public sealed partial class DeviceService
{
    private const string ActionGroup = "Plugs";

    /// <summary>The ids each device has registered now (made again on every rename, method change or merge).</summary>
    private readonly Dictionary<string, List<string>> _registered = new();

    public static string ActionId(DeviceRecord device, DevicePower power) => $"devices.{device.Id}.{Word(power)}";

    public static string ViaActionId(MethodRecord method, DevicePower power) => $"devices.method.{method.Id}.{Word(power)}";

    private static string Word(DevicePower power) => power.ToString().ToLowerInvariant();

    /// <summary>(Re)registers the device's actions: one per power that follows the order, and one per power for each method.</summary>
    private void RegisterActions(DeviceRecord device)
    {
        UnregisterActions(device);
        var name = DisplayName(device);
        var keywords = $"smart device plug bulb socket outlet switch power tapo kasa {device.Name}";
        var icon = IconOf(device);
        var actions = Enum.GetValues<DevicePower>()
            .Select(power => new HotkeyAction(ActionId(device, power), $"{name}: {power}", Category, keywords, ids => _ = RunAsync(device, power), group: ActionGroup) { Icon = icon })
            .Concat(device.Methods.SelectMany(method => Enum.GetValues<DevicePower>().Select(power =>
                new HotkeyAction(ViaActionId(method, power), $"{name}: {power} via {device.NameOf(method)}", Category, $"{keywords} {method.Kind}",
                    ids => _ = RunAsync(device, power, method), group: ActionGroup) { Icon = icon })))
            .ToList();
        _registered[device.Id] = actions.Select(a => a.Id).ToList();
        _hotkeys.Register(actions);
        RegisterState(device);
    }

    /// <summary>The icon the device shows (a library id or a user icon): the one picked for it, else its first real method's template's, else the plug.</summary>
    public string IconOf(DeviceRecord device) =>
        device.Icon.Length > 0 ? Icons.IconLibrary.Current(device.Icon)!
        : device.Methods.FirstOrDefault() is { IsSimulation: false } first && Icons.IconLibrary.Find(ProfileOf(first).Icon) is { } own ? own.Id
        : Icons.IconLibrary.Plug;

    /// <summary>The icon changed: the device's actions and state carry the new glyph.</summary>
    public void IconChanged(DeviceRecord device) => RegisterActions(device);

    private void UnregisterActions(DeviceRecord device)
    {
        if (!_registered.Remove(device.Id, out var ids)) return;
        foreach (var id in ids) _hotkeys.Unregister(id);
        ForgetState(device);
    }

    /// <summary>Takes the shortcuts off the device's actions (before it goes, so its keys are free and none is saved for an id that is gone).</summary>
    private void ClearShortcuts(DeviceRecord device)
    {
        foreach (var id in _registered.GetValueOrDefault(device.Id) ?? new List<string>())
            if (_hotkeys.Find(id) is { Keys.Count: > 0 } action) action.SetKeys(Array.Empty<string>());
    }

    /// <summary>The keys of an action now registered (empty when it has none or is not registered).</summary>
    private IReadOnlyList<string> KeysOf(string id) => _hotkeys.Find(id)?.Keys ?? Array.Empty<string>();

    /// <summary>Adds <paramref name="keys"/> to the action's own (the registry takes each from whichever action had it).</summary>
    private void GiveKeys(string id, IReadOnlyList<string> keys)
    {
        if (keys.Count > 0 && _hotkeys.Find(id) is { } action) action.SetKeys(action.Keys.Concat(keys).Distinct());
    }
}
