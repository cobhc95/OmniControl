using Avalonia.Controls.Templates;
using OmniControl.App.Features.Osd.Pages;

namespace OmniControl.App.Features.Osd;

/// The on-screen display: the pop-up service every feature shows through (<see cref="Osd"/>, made before the other modules) and its page.
/// <param name="listedApps">The apps of the user's app lists, offered by the pop-up duration window's exceptions.</param>
public sealed class OsdModule(Func<IReadOnlyList<RunningProgram>> listedApps) : IFeatureModule
{
    /// The OSD's entry in the master list of hardware watches: the USB devices its "USB devices" pop-up follows.
    private const string UsbWatchId = "osd.usb";

    private readonly OsdService _service = new();
    private FeatureContext? _context;

    public IOsd Osd => _service;

    public void Register(FeatureContext context)
    {
        _context = context;
        context.Hardware.Register(new HardwareWatch(UsbWatchId, "USB devices (OSD)", HardwareSignal.Device, _service.LookAtUsb));
        context.Imports.OnReload(DataStores.Osd, _service.ReloadFromDisk);
        context.Shutdown.Add(ShutdownStage.Osd, "OSD", _service.Dispose);
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage("OSD", () => new OsdPageViewModel(_service, _context!.Actions, _context.Presets, _context.OpenPresetManager, OsdPlacementWindow.AskAsync, listedApps, _context.Report)),
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[] { ViewTemplate.For<OsdPageViewModel, OsdPage>() };
}
