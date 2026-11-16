namespace OmniControl.App.Devices;

// A device's methods (docs/mockups/device-multiple-methods.html): added, removed, changed, put in order, and other devices merged in; and
// the list's own edits (duplicate, reorder). Each change makes the device's actions again, so titles say the right method.
public sealed partial class DeviceService
{
    /// <summary>Adds a method of <paramref name="kind"/> at the end of the device's order; its set-up is still to do.</summary>
    public MethodRecord AddMethod(DeviceRecord device, string kind)
    {
        var method = new MethodRecord { Kind = DeviceMethods.Known(kind) };
        device.Methods.Add(method);
        RegisterActions(device);
        Edited();
        return method;
    }

    /// <summary>Removes one method (never the last: that is deleting the device), with its "via" actions and its password.</summary>
    public void RemoveMethod(DeviceRecord device, MethodRecord method)
    {
        if (device.Methods.Count < 2 || !device.Methods.Contains(method)) return;
        foreach (var power in Enum.GetValues<DevicePower>())
            if (_hotkeys.Find(ViaActionId(method, power)) is { Keys.Count: > 0 } action) action.SetKeys(Array.Empty<string>());
        device.Methods.Remove(method);
        ForgetPassword(method);
        RegisterActions(device);
        Edited();
        ListChanged?.Invoke();
    }

    /// <summary>The method is now of another kind (its setup for each kind is kept on the record); the "via" titles change.</summary>
    public void ChangeKind(DeviceRecord device, MethodRecord method, string kind)
    {
        method.Kind = DeviceMethods.Known(kind);
        RegisterActions(device);
        Edited();
    }

    /// <summary>A drag in the method list: the methods at <paramref name="moved"/> go, in order, in front of the <paramref name="insertAt"/>-th of the ones that stay.</summary>
    public void MoveMethods(DeviceRecord device, IReadOnlyList<int> moved, int insertAt)
    {
        device.Methods = Services.Reorder.Moved(device.Methods, moved, insertAt);
        RegisterActions(device);
        Edited();
    }

    /// <summary>The devices list's Cut and Paste: the devices move, in their order, in front of the <paramref name="insertAt"/>-th of the others.</summary>
    public void MoveDevices(IReadOnlyList<int> moved, int insertAt)
    {
        Settings.Devices = Services.Reorder.Moved(Settings.Devices, moved, insertAt);
        Edited();
        ListChanged?.Invoke();
    }

    /// <summary>
    /// A copy of the device at <paramref name="insertAt"/> in the list, with every method's set-up but no password (each method has a new id,
    /// so its sign-in is typed again) and no shortcuts.
    /// </summary>
    public DeviceRecord Duplicate(DeviceRecord device, int insertAt)
    {
        var copy = device.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = $"{device.Name} copy";
        foreach (var method in copy.Methods) method.Id = Guid.NewGuid().ToString("N");
        copy.IconImage = string.Empty;
        Settings.Devices.Insert(Math.Clamp(insertAt, 0, Settings.Devices.Count), copy);
        if (OwnImagePath(device) is { } image) SetOwnImage(copy, image);
        RegisterActions(copy);
        Edited();
        ListChanged?.Invoke();
        return copy;
    }

    /// <summary>True when a method needs its password typed (a scripts method whose template signs in, with none saved): a copy, for one.</summary>
    public bool NeedsSignIn(DeviceRecord device) => device.Methods.Any(m => !m.IsSimulation && ProfileOf(m).NeedsSignIn && !HasPassword(m));

    /// <summary>
    /// Joins the methods of <paramref name="from"/> (in their order) to the end of <paramref name="into"/>'s, set-ups and passwords kept; the
    /// merged devices are removed, and the shortcuts of their order actions go to the "via" actions of their first method. Every button,
    /// gesture and rule that used a merged device's order action moves to that same "via" action, and a rule on its power to the power of
    /// <paramref name="into"/> (<see cref="Hotkeys.HotkeyRegistry.Move"/>, <see cref="States.StateRegistry.Move"/>).
    /// </summary>
    public void Merge(IReadOnlyList<DeviceRecord> from, DeviceRecord into)
    {
        var movedActions = new Dictionary<string, string>();
        var movedStates = new Dictionary<string, string>();
        foreach (var device in from.Where(d => d != into && Settings.Devices.Contains(d)))
        {
            if (device.Methods.FirstOrDefault() is { } via)
                foreach (var power in Enum.GetValues<DevicePower>()) movedActions[ActionId(device, power)] = ViaActionId(via, power);
            movedStates[StateId(device)] = StateId(into);
            var keys = Enum.GetValues<DevicePower>().ToDictionary(p => p, p => KeysOf(ActionId(device, p)).ToList());
            foreach (var power in Enum.GetValues<DevicePower>())
                if (_hotkeys.Find(ActionId(device, power)) is { Keys.Count: > 0 } action) action.SetKeys(Array.Empty<string>());
            var viaKeys = device.Methods.SelectMany(m => Enum.GetValues<DevicePower>().Select(p => (Id: ViaActionId(m, p), Keys: KeysOf(ViaActionId(m, p)).ToList()))).ToList();
            UnregisterActions(device);
            Settings.Devices.Remove(device);
            _runtime.Remove(device.Id);
            DeleteOwnImage(device);
            var first = device.Methods.FirstOrDefault();
            into.Methods.AddRange(device.Methods);
            RegisterActions(into);
            foreach (var (id, own) in viaKeys) GiveKeys(id, own);
            if (first is not null)
                foreach (var power in Enum.GetValues<DevicePower>()) GiveKeys(ViaActionId(first, power), keys[power]);
        }
        Edited();
        ListChanged?.Invoke();
        _hotkeys.Move(movedActions);
        _states.Move(movedStates);
    }
}
