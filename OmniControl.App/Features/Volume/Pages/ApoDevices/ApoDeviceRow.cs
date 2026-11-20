using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Pages.ApoDevices;

/// A muted group heading of the device window ("Playback devices", "Recording devices").
public sealed record ApoDeviceHeading(string Text);

/// How a device row's status reads: plain (not installed), green (running), red (installed, not running), accent (a step runs on it).
public enum ApoRowTone { Plain, Good, Bad, Working }

/// What a row asks of its window: select it, open or close its confirmation, run its confirmed step, show its record.
public sealed record ApoRowHost(Action<ApoDeviceRow> Select, Action<ApoDeviceRow, bool> Ask, Action<ApoDeviceRow> Confirm, Action<ApoDeviceRow> Details);

/// One row of the device window: Device (connector, device name under it), Status (one main line, one muted detail line) and Action
/// (Details while it does not run, Install or Uninstall), with its inline confirmation under it.
public sealed partial class ApoDeviceRow : ObservableObject
{
    private readonly ApoRowHost _host;

    public ApoDeviceRow(string key, ApoRowHost host) => (Key, _host) = (key, host);

    /// The device's MMDevices key name.
    public string Key { get; }

    public ApoDeviceEntry? Entry { get; private set; }

    [ObservableProperty] private string _connector = string.Empty;
    [ObservableProperty] private string _device = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDetail))] private string _detail = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsGood), nameof(IsBad), nameof(IsWorking), nameof(IsPlain))] private ApoRowTone _tone;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstall), nameof(CanUninstall))] private bool _attached;
    [ObservableProperty] private bool _showsDetails;
    [ObservableProperty] private bool _isSelected;

    /// No step runs in the window, so its buttons work.
    [ObservableProperty] private bool _canAct = true;

    /// The confirmation under the row is open (for <see cref="Attached"/>: Uninstall, else Install).
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ConfirmTitle), nameof(ConfirmText), nameof(ConfirmLabel))] private bool _isConfirming;

    public bool HasDetail => Detail.Length > 0;
    public bool IsGood => Tone == ApoRowTone.Good;
    public bool IsBad => Tone == ApoRowTone.Bad;
    public bool IsWorking => Tone == ApoRowTone.Working;
    public bool IsPlain => Tone == ApoRowTone.Plain;
    public bool CanInstall => !Attached;
    public bool CanUninstall => Attached;

    private string Name => Connector.Length > 0 ? Connector : Device;
    public string ConfirmTitle => Attached ? $"Uninstall Equalizer APO from {Name}?" : $"Install Equalizer APO on {Name}?";
    public string ConfirmText => Attached
        ? "Windows asks once for administrator rights, sound stops for a few seconds, and the device gets its own effects back."
        : "Windows asks once for administrator rights, sound stops for a few seconds, then OmniControl checks it runs.";
    public string ConfirmLabel => Attached ? "Uninstall" : "Install";

    [RelayCommand] private void Select() => _host.Select(this);
    [RelayCommand] private void Act() => _host.Ask(this, true);
    [RelayCommand] private void Cancel() => _host.Ask(this, false);
    [RelayCommand] private void Confirm() => _host.Confirm(this);
    [RelayCommand] private void ShowDetails() => _host.Details(this);

    /// Shows what is in place.
    public void Show(ApoDeviceEntry entry, string status, string detail, ApoRowTone tone)
    {
        Entry = entry;
        (Connector, Device, Status, Detail, Tone, Attached) = (entry.Connector, entry.Device, status, detail, tone, entry.Attached);
        ShowsDetails = entry.Attached && tone == ApoRowTone.Bad;
    }

    /// Shows the step that runs on it.
    public void Working(string status, string steps) => (Status, Detail, Tone, ShowsDetails) = (status, steps, ApoRowTone.Working, false);
}
