namespace OmniControl.App.Features.Hotkeys;

/// A device a hotkey can target (a monitor, a speaker...).
public sealed record HotkeyDevice(string Id, string Name);

/// A chip in a hotkey's "Applies to" group: either "All ..." or one device.
public sealed partial class ScopeChip : ObservableObject
{
    private readonly Action<ScopeChip> _toggled;

    public string? DeviceId { get; }
    public string Name { get; }
    public bool IsAll => DeviceId is null;

    [ObservableProperty] private bool _isOn;

    public ScopeChip(string? deviceId, string name, bool isOn, Action<ScopeChip> toggled)
    {
        DeviceId = deviceId;
        Name = name;
        _isOn = isOn;
        _toggled = toggled;
    }

    partial void OnIsOnChanged(bool value) => _toggled(this);
}
