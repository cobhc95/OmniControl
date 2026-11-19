using Avalonia.Controls.Templates;
using OmniControl.App.Features.Automation.Pages;

namespace OmniControl.App.Features.Automation;

/// Automation: the rules, which register their own actions in the master list and are the one gate every action passes, and the page.
/// <param name="rememberedPrograms">The program paths the mixer remembers, offered as programs to choose.</param>
public sealed class AutomationModule(Func<IEnumerable<string>> rememberedPrograms) : IFeatureModule
{
    /// The rules' entry in the master list of hardware watches: the USB devices a "device connected / disconnected" rule follows.
    private const string UsbWatchId = "automation.usb";

    private AutomationService? _service;
    private FeatureContext? _context;

    public void Register(FeatureContext context)
    {
        _context = context;
        _service = new AutomationService(context.Actions, context.Runner, context.ActionGate, context.States, CountdownWindow.ConfirmAsync, rememberedPrograms,
            StartupRegistration.IsBackgroundStart(Environment.GetCommandLineArgs().Skip(1)), context.Report, context.Power);
        context.Hardware.Register(new HardwareWatch(UsbWatchId, "USB devices (rules)", HardwareSignal.Device, _service.LookAtUsb));
        context.Imports.OnReload(DataStores.Automation, _service.ReloadFromDisk);
        context.Shutdown.Add(ShutdownStage.Automation, "automation", _service.Dispose);
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage("Automation", () => new AutomationPageViewModel(_service!, _context!.AskAsync)),
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<AutomationPageViewModel, AutomationPage>(),
        ViewTemplate.For<AutomationItem, AutomationRuleRow>(),
        ViewTemplate.For<CustomActionItem, CustomActionRow>(),
    };
}
