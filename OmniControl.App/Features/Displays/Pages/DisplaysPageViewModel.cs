namespace OmniControl.App.Features.Displays.Pages;

/// The Displays page, made at its first use with its cards, views of the models of <see cref="DisplaysService"/> (the module's, which owns the
/// displays and the page's actions, states, presets and watches): a Hardware group (what each monitor itself can do, over DDC/CI, and a
/// laptop's panel) and a Software group (what OmniControl does to the picture on any display), each holding one collapsed group per
/// display, the two preset pickers and Refresh. The displays are looked at again when the page shows and whenever the window is shown or hidden.
public sealed partial class DisplaysPageViewModel : PageViewModel, IDisposable
{
    private const string NoMonitors = "No monitors available";

    private readonly DisplaysService _service;
    private readonly Dictionary<HardwareDisplayModel, HardwareDisplayItem> _hardware = new();
    private readonly Dictionary<SoftwareDisplayModel, SoftwareDisplayItem> _software = new();
    private readonly SoftwareGlobalItem _desktop;
    private readonly WarmLightItem _warm;
    private readonly ProviderChoice _pictures, _monitors;

    public static readonly PageInfo Info = new(PageTitles.Displays,
        "M4,4 H20 A2,2 0 0 1 22,6 V16 A2,2 0 0 1 20,18 H14 V20 H17 V22 H7 V20 H10 V18 H4 A2,2 0 0 1 2,16 V6 A2,2 0 0 1 4,4 Z M4,6 V16 H20 V6 Z", NavGroups.Devices,
        "monitor display screen brightness contrast ddc laptop panel gamma digital saturation temperature warm cool input source colour color", "displays");

    public DisplaysPageViewModel(DisplaysService service) : base(Info)
    {
        _service = service;
        _desktop = new SoftwareGlobalItem(service.Desktop, service.Report);
        _warm = new WarmLightItem(service.WarmLight, service.Report);
        Hardware.UseSwitch(service.ScreenMaster());
        Software = new ParentGroup("Displays/Software", DisplayControls.SoftwareGroup, string.Empty, _desktop.Cards.Concat(_warm.Cards).Cast<object>().ToList())
        {
            ExpandedByDefault = true,
            Restore = service.ResetAllSoftware,
            RestoreTip = SoftwareRestoreTip,
            Tip = SoftwareTip,
        };
        _pictures = new ProviderChoice(service.PicturePresetsLabel, service.PicturePresets, service.ManagePicturePresets, withCustom: true);
        _monitors = new ProviderChoice(service.MonitorPresetsLabel, service.MonitorPresets, service.ManageMonitorPresets, withCustom: true);
        service.HardwareListChanged += ShowHardware;
        service.HardwareValuesChanged += ShowHardwareValues;
        service.SoftwareListChanged += ShowSoftware;
        service.PictureChanged += _pictures.Refresh;
        ShowHardware();
        ShowHardwareValues();
        ShowSoftware();
    }

    public override string KeyHelp => "F5 looks at the monitors again (Refresh).";

    /// What the monitors themselves can do: one inner group per display.
    public ParentGroup Hardware { get; } = new("Displays/Hardware", DisplayControls.HardwareGroup, NoMonitors)
    {
        SummaryBesideName = true,
        ExpandedByDefault = true,
        Tip = HardwareTip,
    };

    /// What OmniControl does to the picture itself: the whole-desktop controls, then one inner group per display.
    public ParentGroup Software { get; }

    /// The preset drop-downs on the Software and Hardware groups' title lines.
    public ChoiceItem PresetChoice => _pictures.Choice;
    public ChoiceItem MonitorPresetChoice => _monitors.Choice;

    /// The monitors came, went or one was read: their groups follow, each keeping its cards.
    private void ShowHardware()
    {
        var models = _service.HardwareDisplays;
        foreach (var gone in _hardware.Keys.Except(models).ToList())
        {
            _hardware[gone].Dispose();
            _hardware.Remove(gone);
        }
        foreach (var model in models)
        {
            if (_hardware.TryGetValue(model, out var item)) item.Show();
            else _hardware[model] = new HardwareDisplayItem(model, _service.Report, _service.NewRotationKeysSwitch());
        }
        Hardware.Subgroups.Clear();
        foreach (var group in models.SelectMany(m => _hardware[m].Groups)) Hardware.Subgroups.Add(group);
        Hardware.Summary = models.Count switch
        {
            0 => NoMonitors,
            1 => "1 monitor available",
            _ => $"{models.Count} monitors available",
        };
    }

    /// A monitor's value or a screen's power changed: the power switches and the monitor preset drop-down look again.
    private void ShowHardwareValues()
    {
        foreach (var item in _hardware.Values) item.RefreshMaster();
        Hardware.RefreshMaster();
        _monitors.Refresh();
    }

    /// The software displays came or went: new ones get their group, gone ones lose it, the others keep theirs.
    private void ShowSoftware()
    {
        var models = _service.SoftwareDisplays;
        foreach (var gone in _software.Keys.Except(models).ToList())
        {
            _software[gone].Dispose();
            _software.Remove(gone);
        }
        foreach (var model in models)
            if (!_software.ContainsKey(model)) _software[model] = new SoftwareDisplayItem(model, _service.Report);
        Software.Subgroups.Clear();
        foreach (var model in models) Software.Subgroups.Add(_software[model].Group);
    }

    public override void OnShown() => _service.Rescan();

    public override void OnWindowShownChanged(bool shown) => _service.Rescan();

    /// Opens every monitor again and asks each what it supports (the Refresh button, F5).
    [RelayCommand]
    private void Reload() => _service.Reload();

    public void Dispose()
    {
        _service.HardwareListChanged -= ShowHardware;
        _service.HardwareValuesChanged -= ShowHardwareValues;
        _service.SoftwareListChanged -= ShowSoftware;
        _service.PictureChanged -= _pictures.Refresh;
        foreach (var item in _hardware.Values) item.Dispose();
        foreach (var item in _software.Values) item.Dispose();
        _desktop.Dispose();
        _warm.Dispose();
        _pictures.Dispose();
        _monitors.Dispose();
    }
}
