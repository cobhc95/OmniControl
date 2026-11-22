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

    public static readonly PageInfo Info = new("Displays",
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
            RestoreTip = "Puts every display's software picture, saturation, grayscale, invert and the warm-light schedule back to normal. Presets are kept.",
            Tip = "Applied by OmniControl on any display and put back when it closes (the saved presets and the warm-light schedule are kept). "
                  + "Colour temperature: minus is warmer, plus is cooler. Saturation, grayscale and invert apply to all displays. "
                  + "A display whose driver does not accept colour changes from programs is greyed out.",
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
        Tip = "Talks to each monitor over DDC/CI (it must be on in the monitor's own menu); controls a monitor does not report are greyed out. "
              + "Turn screen off sends it to sleep: wake it with its button or by using its input, as the monitor allows.",
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

    /// Only the controls a monitor has are searched for; the ones it does not report are not offered.
    public override IEnumerable<SearchEntry> SearchEntries =>
        _hardware.Values.SelectMany(h => h.AvailableCards.Select(c => Entry(c, HardwareCard(c.Label, h.Name))))
            .Concat(Software.Items.OfType<IEditorItem>().Select(c => Entry(c, DesktopCard(c.Label))))
            .Concat(_software.Values.SelectMany(s => s.Cards.Select(c => Entry(c, SoftwareCard(c.Label, s.Name)))))
            .Append(Entry(PresetChoice, PicturePresetsCard(PresetChoice.Label)))
            .Append(Entry(MonitorPresetChoice, MonitorPresetsCard(MonitorPresetChoice.Label)));

    /// The page's search words (<c>FeaturePage.Words</c>), from the service's models, the same as <see cref="SearchEntries"/> finds; none while the service does not run.
    public static IEnumerable<string> Words(DisplaysService? service) =>
        service is null ? Array.Empty<string>() :
            service.HardwareDisplays.SelectMany(h => h.AvailableLabels.Select(l => HardwareCard(l, h.Name).Words))
                .Concat(DesktopPictureModel.Labels.Concat(WarmLightModel.Labels).Select(l => DesktopCard(l).Words))
                .Concat(service.SoftwareDisplays.SelectMany(s => DisplayControls.Software.Select(l => SoftwareCard(l.Spec.Label, s.Name).Words)))
                .Append(PicturePresetsCard(service.PicturePresetsLabel).Words)
                .Append(MonitorPresetsCard(service.MonitorPresetsLabel).Words);

    private static SearchCard HardwareCard(string label, string display) =>
        new(label, $"monitor display screen ddc {display}", DisplayControls.HardwareGroup, $"{label} ({display})");

    private static SearchCard DesktopCard(string label) => new(label, "monitor display screen digital software", DisplayControls.SoftwareGroup);

    private static SearchCard SoftwareCard(string label, string display) =>
        new(label, $"monitor display screen digital software {display}", DisplayControls.SoftwareGroup, $"{label} ({display})");

    private static SearchCard PicturePresetsCard(string label) =>
        new(label, "display presets manage save apply picture preset cogwheel settings", DisplayControls.SoftwareGroup, "Display presets");

    private static SearchCard MonitorPresetsCard(string label) =>
        new(label, "monitor presets manage save apply hardware ddc preset cogwheel settings", DisplayControls.HardwareGroup, "Monitor presets");

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
