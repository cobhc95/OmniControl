using Avalonia.Controls.Templates;
using OmniControl.App.Features.Devices.Pages;
using OmniControl.App.Features.Osd;
using OmniControl.App.Features.OtherPcs;

namespace OmniControl.App.Features.Devices;

/// Smart Devices: the device service (read at start-up and by its periodic checks, so the tray tiles are right before the page opens) and its page.
/// <param name="pairedPcs">The paired PCs a device set-up can be imported from; null while Other PCs is off (the page has no import group then).</param>
/// <param name="openOtherPcs">Opens the Other PCs page.</param>
public sealed class DevicesModule(Func<IPairedPcs?> pairedPcs, Action openOtherPcs) : IFeatureModule
{
    private DeviceService? _service;
    private FeatureContext? _context;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Devices };

    public void Register(FeatureContext context)
    {
        _context = context;
        context.Osd.DeclareText(OsdService.SmartDevicesElement);
        var service = _service = new DeviceService(context.Actions, context.States, context.Hardware, new WindowsCredentialStore(), context.Report, context.Osd.ShowDeviceNote,
            context.Rules);
        context.Imports.OnReload(DataStores.Devices, service.ReloadFromDisk);
        context.Imports.OnFact(DataStores.Devices.Import!.Area, service.PasswordsNeededIn);
        context.Shutdown.AddWhenOff(ShutdownStage.Pages, "smart devices", () =>
        {
            service.Dispose();
            _service = null;
        });
    }

    /// The device set-ups this PC shares with a paired PC (<paramref name="includeScripts"/>: also the user's own script templates); while
    /// Smart devices is off, none.
    public string ShareSetups(bool includeScripts) => _service is { } service ? service.ShareSetups(includeScripts) : DeviceShareFormat.Write(new DeviceSettings(), includeScripts);

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(DevicesPageViewModel.Info, () => new DevicesPageViewModel(_service!, _context!.AskAsync, _context.Report, _context.IconPicker.ChooseAsync, pairedPcs(), openOtherPcs,
            () => _context.OpenPage(DevicesPageViewModel.Info.Title, null)), () => DevicesPageViewModel.Words(_service)),
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<DevicesPageViewModel, DevicesPage>(),
        ViewTemplate.For<DeviceItem, DeviceDetail>(),
    };
}
