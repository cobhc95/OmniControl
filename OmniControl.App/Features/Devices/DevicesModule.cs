using Avalonia.Controls.Templates;
using OmniControl.App.Features.Devices.Pages;
using OmniControl.App.Features.OtherPcs;

namespace OmniControl.App.Features.Devices;

/// Smart Devices: the device service (read at start-up and by its periodic checks, so the tray tiles are right before the page opens) and its page.
/// <param name="pairedPcs">The paired PCs a device set-up can be imported from.</param>
/// <param name="openOtherPcs">Opens the Other PCs page.</param>
public sealed class DevicesModule(Func<IPairedPcs> pairedPcs, Action openOtherPcs) : IFeatureModule
{
    private DeviceService? _service;
    private FeatureContext? _context;

    public void Register(FeatureContext context)
    {
        _context = context;
        _service = new DeviceService(context.Actions, context.States, context.Hardware, new WindowsCredentialStore(), context.Report, context.Osd.ShowDeviceNote);
        Guard.Run("Start-up: device auto-checks", _service.AutoChecking.Start);
        context.Imports.OnReload(DataStores.Devices, _service.ReloadFromDisk);
        context.Imports.OnFact(DataStores.Devices.Import!.Area, _service.PasswordsNeededIn);
    }

    /// The device set-ups this PC shares with a paired PC (<paramref name="includeScripts"/>: also the user's own script templates).
    public string ShareSetups(bool includeScripts) => _service!.ShareSetups(includeScripts);

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage("Devices", () => new DevicesPageViewModel(_service!, _context!.AskAsync, _context.Report, _context.IconPicker.ChooseAsync, pairedPcs(), openOtherPcs)),
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<DevicesPageViewModel, DevicesPage>(),
        ViewTemplate.For<DeviceItem, DeviceDetail>(),
    };
}
