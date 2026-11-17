using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Devices;

namespace OmniControl.App.Pages.Devices;

// Reading the device's real state (docs/mockups/device-autocheck.html): the Check now pill and the Auto-check chip of the Ready view.
// What is read and when it runs on its own is DeviceService.Check.cs; this only asks for a read on the pill and edits the choice.
public sealed partial class DeviceItem
{
    private AutoCheck? AutoCheckValue => _service.AutoCheckOf(Record);

    /// <summary>A device with no Scripts method is checked by hand only, so its chip is disabled.</summary>
    public bool AutoCheckEditable => AutoCheckValue is not null;

    public string AutoCheckLabel => AutoCheckValue is { } value ? $"Auto-check: {ShortName(value)}" : $"Auto-check: {AutoChecks.ManualOnly}";

    public string AutoCheckTip => AutoCheckEditable
        ? "How often the device's real state is read (a switch by hand is not seen otherwise)."
        : "Checking by simulation is slow and heavy, so it only runs when you press Check now.";

    /// <summary>The chip's short words: the choice's name without its lead-in ("every 5 min", "off").</summary>
    private static string ShortName(AutoCheck value) => value switch
    {
        AutoCheck.StartAndEvery5 => "start, every 5 min",
        AutoCheck.StartAndEvery15 => "start, every 15 min",
        AutoCheck.StartOnly => "start only",
        AutoCheck.WhenOpens => "when opened",
        _ => "off",
    };

    /// <summary>The Auto-check menu: every choice of the master list with a tick on the device's, then the "Same for every device" switch and the Scripts-only note.</summary>
    public IReadOnlyList<MenuRowItem> AutoCheckOptions =>
        AutoChecks.All.Select(c => new MenuRowItem(c.Name, string.Empty, c.Value == AutoCheckValue, string.Empty, new RelayCommand(() => _service.PickAutoCheck(Record, c.Value))))
            .Append(new MenuRowItem("Also when the tray panel opens", string.Empty, _service.AutoCheckOnTrayOf(Record), "Reads the state (Scripts only) a moment after the tray panel opens.",
                new RelayCommand(() => _service.PickAutoCheckOnTray(Record, !_service.AutoCheckOnTrayOf(Record))), RuleAbove: true, IsSwitch: true))
            .Append(new MenuRowItem("Same for every device", string.Empty, _service.AutoCheckSame, "On: a change applies to all devices. Off: this device only.",
                new RelayCommand(() => _service.SetAutoCheckSame(Record, !_service.AutoCheckSame)), IsSwitch: true))
            .Append(MenuRowItem.Header("Uses Scripts only. Simulation is never used for checks."))
            .ToList();

    private void OnAutoCheckChanged()
    {
        OnPropertyChanged(nameof(AutoCheckLabel));
        OnPropertyChanged(nameof(AutoCheckTip));
        OnPropertyChanged(nameof(AutoCheckEditable));
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckNow()
    {
        await _service.CheckStateAsync(Record);
        Refresh();
    }
}
