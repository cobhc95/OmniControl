using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Devices;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Devices;

// Reading the device's real state (docs/mockups/device-state-refresh.html): the Check now pill and the Auto-check chip of the Ready view.
// What is read and how is DeviceService.Check.cs; this only asks for it, on the pill, when the page opens and on the chosen interval.
public sealed partial class DeviceItem
{
    private DispatcherTimer? _autoCheck;

    private AutoCheck AutoCheckValue => Record.AutoCheck ?? AutoChecks.DefaultFor(Record.FirstKind);

    public string AutoCheckLabel => $"Auto-check: {AutoChecks.Of(AutoCheckValue).Name.ToLowerInvariant()}";

    public string AutoCheckTip => Record.FirstKind == DeviceMethods.Simulation
        ? "How often the device's real state is read. A periodic check keeps the emulator running."
        : "How often the device's real state is read (a switch by hand is not seen otherwise).";

    /// <summary>The Auto-check menu: every choice of the master list with a tick on the device's.</summary>
    public IReadOnlyList<MenuRowItem> AutoCheckOptions =>
        AutoChecks.All.Select(c => new MenuRowItem(c.Name, string.Empty, c.Value == AutoCheckValue, string.Empty, new RelayCommand(() => PickAutoCheck(c.Value)))).ToList();

    private void PickAutoCheck(AutoCheck value)
    {
        Record.AutoCheck = value;
        _service.Edited();
        StartAutoCheck();
        OnPropertyChanged(nameof(AutoCheckLabel));
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckNow()
    {
        await _service.CheckStateAsync(Record);
        Refresh();
    }

    /// <summary>The page was opened: a device set to "When page opens" reads its state (the tools it needs are probed first).</summary>
    public async Task CheckWhenShownAsync()
    {
        if (AutoCheckValue == AutoCheck.WhenOpens) await CheckQuietlyAsync();
    }

    /// <summary>(Re)starts the interval timer for the device's choice; none for Off and When page opens.</summary>
    private void StartAutoCheck()
    {
        StopAutoCheck();
        if (StartupMode.Inert || AutoChecks.Of(AutoCheckValue).Every is not { } every) return; // a screenshot export reads no device
        _autoCheck = new DispatcherTimer { Interval = every };
        _autoCheck.Tick += async (_, _) => await CheckQuietlyAsync();
        _autoCheck.Start();
    }

    private void StopAutoCheck()
    {
        _autoCheck?.Stop();
        _autoCheck = null;
    }

    /// <summary>An automatic check: only when the device is ready, not in set-up and able to be read; a problem shows in the foot line, never as a pop-up.</summary>
    private async Task CheckQuietlyAsync()
    {
        if (!IsReady || InSetup || !CanCheck) return;
        await _service.CheckStateAsync(Record);
        Refresh();
    }
}
