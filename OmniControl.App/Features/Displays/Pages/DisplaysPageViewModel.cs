using OmniControl.App.Shared.Ui.Restore;

namespace OmniControl.App.Features.Displays.Pages;

/// The Displays page, made at its first use: the Hardware and Software groups of <see cref="DisplaysService"/> (the module's, which owns the
/// displays, their cards and the page's actions, states, presets and watches), the two preset pickers and Refresh. The displays are looked
/// at again when the page shows and whenever the window is shown or hidden.
public sealed partial class DisplaysPageViewModel : PageViewModel
{
    private readonly DisplaysService _service;
    private IRestorable? _restorable;

    public static readonly PageInfo Info = new("Displays",
        "M4,4 H20 A2,2 0 0 1 22,6 V16 A2,2 0 0 1 20,18 H14 V20 H17 V22 H7 V20 H10 V18 H4 A2,2 0 0 1 2,16 V6 A2,2 0 0 1 4,4 Z M4,6 V16 H20 V6 Z", NavGroups.Devices,
        "monitor display screen brightness contrast ddc laptop panel gamma digital saturation temperature warm cool input source colour color", "displays");

    public DisplaysPageViewModel(DisplaysService service) : base(Info) => _service = service;

    public override string KeyHelp => "F5 looks at the monitors again (Refresh).";

    /// What the monitors themselves can do: one inner group per display.
    public ParentGroup Hardware => _service.Hardware;

    /// What OmniControl does to the picture itself: the whole-desktop controls, then one inner group per display.
    public ParentGroup Software => _service.Software;

    /// The preset drop-downs on the Software and Hardware groups' title lines.
    public ChoiceItem PresetChoice => _service.PresetChoice;
    public ChoiceItem MonitorPresetChoice => _service.MonitorPresetChoice;

    public override IRestorable? Restorable => _restorable ??= _service.NewRestorable(Info.RestoreKey!);

    /// Only the controls a monitor has are searched for; the ones it does not report are not offered.
    public override IEnumerable<SearchEntry> SearchEntries =>
        _service.HardwareDisplays.SelectMany(h => h.AvailableCards.Select(c => Entry(c, $"{c.Label} ({h.Name})", $"monitor display screen ddc {h.Name}", DisplayControls.HardwareGroup)))
            .Concat(_service.WholeDesktopCards.Select(c => Entry(c, c.Label, "monitor display screen digital software", DisplayControls.SoftwareGroup)))
            .Concat(_service.SoftwareDisplays.SelectMany(s => s.Cards.Select(c =>
                Entry(c, $"{c.Label} ({s.Name})", $"monitor display screen digital software {s.Name}", DisplayControls.SoftwareGroup))))
            .Append(Entry(PresetChoice, "Display presets", "display presets manage save apply picture preset cogwheel settings", DisplayControls.SoftwareGroup))
            .Append(Entry(MonitorPresetChoice, "Monitor presets", "monitor presets manage save apply hardware ddc preset cogwheel settings", DisplayControls.HardwareGroup));

    public override void OnShown() => _service.Rescan();

    public override void OnWindowShownChanged(bool shown) => _service.Rescan();

    /// Opens every monitor again and asks each what it supports (the Refresh button, F5).
    [RelayCommand]
    private void Reload() => _service.Reload();
}
