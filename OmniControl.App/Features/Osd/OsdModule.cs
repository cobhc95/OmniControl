using Avalonia.Controls.Templates;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd.Pages;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Osd;

/// The on-screen display: the pop-up service every feature shows through (<see cref="Osd"/>, made before the other modules and kept while the
/// feature is off, when it shows nothing) and its page.
/// <param name="listedApps">The apps of the user's app lists, offered by the pop-up duration window's exceptions.</param>
public sealed class OsdModule(Func<IReadOnlyList<RunningProgram>> listedApps) : IFeatureModule, IDisposable
{
    /// The OSD's entry in the master list of hardware watches: the USB devices its "USB devices" pop-up follows.
    private const string UsbWatchId = "osd.usb";

    private readonly OsdService _service = new();
    private FeatureContext? _context;

    public IOsd Osd => _service;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Osd };

    /// Takes back an element a feature declared (that feature was switched off).
    public void Forget(string element) => _service.Forget(element);

    public void Register(FeatureContext context)
    {
        _context = context;
        _service.UseFeature(true);
        context.Hardware.Register(new HardwareWatch(UsbWatchId, "USB devices (OSD)", HardwareSignal.Device, _service.LookAtUsb));
        context.Imports.OnReload(DataStores.Osd, _service.ReloadFromDisk);
        context.Shutdown.AddWhenOff(ShutdownStage.Osd, "OSD off", () => _service.UseFeature(false));
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(OsdPageViewModel.Info, () => new OsdPageViewModel(_service, _context!.OpenPresetManager, OsdPlacementWindow.AskAsync, listedApps, _context.Report), () => OsdPageViewModel.Words(_service))
        {
            Registers = RegisterPageEntries,
        },
    };

    /// The page's named places (presets, the same provider pattern as the layers: the preset manager lists, renames and reorders them) and actions.
    private void RegisterPageEntries()
    {
        var title = OsdPageViewModel.Info.Title;
        _context!.Presets.Register(new PresetGroup(OsdPlacePresets.GroupId, title, "Places", new OsdPlacePresets(_service)));
        var actions = _context.Actions;
        actions.Register(new HotkeyAction("osd.toggle", "Toggle OSD", title, "osd on-screen display overlay popup", _ => _service.ToggleEnabled()));
        actions.Register(new HotkeyAction("osd.style.next", "Next OSD style", title, "osd on-screen display style look", _ => _service.StepStyle()));
        actions.Register(new HotkeyAction("osd.windows.toggle", "Toggle hiding Windows' OSD", title, "osd windows volume brightness popup", _ => _service.ToggleHideWindowsOsd()));
    }

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[] { ViewTemplate.For<OsdPageViewModel, OsdPage>() };

    /// The service itself goes only at exit: every feature holds it.
    public void Dispose() => _service.Dispose();
}
