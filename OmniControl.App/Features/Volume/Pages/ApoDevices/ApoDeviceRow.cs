using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Pages.ApoDevices;

/// A muted group heading of the device window ("Playback devices", "Recording devices").
public sealed record ApoDeviceHeading(string Text);

/// How a device row's status reads.
public enum ApoRowTone { Plain, Good, Caution, Bad }

/// One row of the device window (Device Selector's row): its tick, Connector, Device and Status.
public sealed partial class ApoDeviceRow : ObservableObject
{
    private readonly Action<ApoDeviceRow, bool> _toggled;
    private readonly Action<ApoDeviceRow> _select;
    private bool _syncing;

    public ApoDeviceRow(string key, Action<ApoDeviceRow, bool> toggled, Action<ApoDeviceRow> select) => (Key, _toggled, _select) = (key, toggled, select);

    /// The device's MMDevices key name.
    public string Key { get; }

    public ApoDeviceEntry? Entry { get; private set; }

    [ObservableProperty] private string _connector = string.Empty;
    [ObservableProperty] private string _device = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPlain), nameof(IsGood), nameof(IsCaution), nameof(IsBad))] private ApoRowTone _tone;
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isSelected;

    public bool IsPlain => Tone == ApoRowTone.Plain;
    public bool IsGood => Tone == ApoRowTone.Good;
    public bool IsCaution => Tone == ApoRowTone.Caution;
    public bool IsBad => Tone == ApoRowTone.Bad;

    [RelayCommand]
    private void Select() => _select(this);

    /// Shows what is in place, without treating it as the user's tick.
    public void Show(ApoDeviceEntry entry, string status, ApoRowTone tone)
    {
        Entry = entry;
        (Connector, Device, Status, Tone) = (entry.Connector, entry.Device, status, tone);
        _syncing = true;
        IsChecked = entry.Attached;
        _syncing = false;
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_syncing) _toggled(this, value);
    }
}
