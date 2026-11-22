using Avalonia.Controls.Templates;
using OmniControl.App.Features.Automation.Pages;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Automation;

/// Automation: the rules, which register their own actions in the master list and are the one gate every action passes, and the page. Its
/// feature, "Hotkeys and automation", also has the keyboard hotkeys registered with Windows while it runs (the master list of actions stays).
/// <param name="rememberedPrograms">The program paths the mixer remembers, offered as programs to choose.</param>
/// <param name="useKeys">Registers the keyboard hotkeys with Windows (true) or none (<see cref="Hotkeys.HotkeysSwitch.Use"/>).</param>
/// <param name="presetTriggers">Where the rules answer the preset manager's trigger chips.</param>
public sealed class AutomationModule(Func<IEnumerable<string>> rememberedPrograms, Action<bool> useKeys, PresetTriggersSlot presetTriggers) : IFeatureModule
{
    /// The rules' entry in the master list of hardware watches: the USB devices a "device connected / disconnected" rule follows.
    private const string UsbWatchId = "automation.usb";

    private AutomationService? _service;
    private FeatureContext? _context;
    private AutomationPresets? _presets;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Automation };

    public void Register(FeatureContext context)
    {
        _context = context;
        var service = _service = new AutomationService(context.Actions, context.Runner, context.States, CountdownWindow.ConfirmAsync, rememberedPrograms,
            context.Report, context.Power, context.AskAsync);
        // The one gate every action passes (its block rules) and the preset manager's trigger chips plug in here, and leave first at shut-down.
        context.ActionGate.Attach(service);
        var presets = _presets = new AutomationPresets(service, context.Actions, context.Runner, presetTriggers, context.OpenPresetManager);
        context.Hardware.Register(new HardwareWatch(UsbWatchId, "USB devices (rules)", HardwareSignal.Device, service.LookAtUsb));
        context.Imports.OnReload(DataStores.Automation, service.ReloadFromDisk);
        context.Shutdown.Add(ShutdownStage.Automation, "automation", () =>
        {
            context.ActionGate.Detach(service);
            presets.Dispose();
            service.Dispose();
        });
        useKeys(true);
        context.Shutdown.AddWhenOff(ShutdownStage.HotkeysSwitch, "keyboard hotkeys", () => useKeys(false));
    }

    public IReadOnlyList<FeaturePage> Pages => new[]
    {
        new FeaturePage(AutomationPageViewModel.Info, () => new AutomationPageViewModel(_service!, _context!.AskAsync, _presets), () => AutomationPageViewModel.Words(_service)),
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<AutomationPageViewModel, AutomationPage>(),
        ViewTemplate.For<AutomationItem, AutomationRuleRow>(),
        ViewTemplate.For<CustomActionItem, CustomActionRow>(),
    };
}
