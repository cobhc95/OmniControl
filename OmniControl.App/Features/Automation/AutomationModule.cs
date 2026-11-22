using Avalonia.Controls.Templates;
using OmniControl.App.Features.Automation.Pages;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Automation;

/// Automation: the rules, which register their own actions in the master list and are the one gate every action passes, and the page. It runs
/// while its feature, "Hotkeys and automation", is off too, so the rules of other pages (warm light, network limits, device checks) keep
/// running; on, it also runs the user's own and preset rules, the blocks, the page and the keyboard hotkeys registered with Windows.
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
    private AutomationFeatureRules? _features;

    public IReadOnlyList<string> Features { get; } = new[] { FeatureList.Automation };

    public bool RunsWhenOff => true;

    public void Register(FeatureContext context)
    {
        _context = context;
        var on = context.Features.IsOn(FeatureList.Automation);
        var service = _service = new AutomationService(context.Actions, context.Runner, context.States, CountdownWindow.ConfirmAsync, rememberedPrograms,
            context.Report, context.Power, context.AskAsync, on);
        var features = _features = new AutomationFeatureRules(service, context.Actions, context.Rules, context.OpenPage);
        context.Hardware.Register(new HardwareWatch(UsbWatchId, "USB devices (rules)", HardwareSignal.Device, service.LookAtUsb));
        context.Imports.OnReload(DataStores.Automation, service.ReloadFromDisk);
        AutomationPresets? presets = _presets = null;
        if (on)
        {
            // The one gate every action passes (its block rules) and the preset manager's trigger chips plug in here, and leave first at shut-down.
            context.ActionGate.Attach(service);
            presets = _presets = new AutomationPresets(service, context.Actions, context.Runner, presetTriggers, features, context.OpenPresetManager);
        }
        context.Shutdown.Add(ShutdownStage.Automation, "automation", () =>
        {
            if (on) context.ActionGate.Detach(service);
            presets?.Dispose();
            features.Dispose();
            service.Dispose();
        });
        if (!on) return;
        useKeys(true);
        context.Shutdown.AddWhenOff(ShutdownStage.HotkeysSwitch, "keyboard hotkeys", () => useKeys(false));
    }

    /// The page only while the feature is on.
    public IReadOnlyList<FeaturePage> Pages => _service is not { UserRules: true } ? Array.Empty<FeaturePage>() : new[]
    {
        new FeaturePage(AutomationPageViewModel.Info, () => new AutomationPageViewModel(_service!, _context!.AskAsync, _presets, _features),
            () => AutomationPageViewModel.Words(_service, _features)),
    };

    public IReadOnlyList<IDataTemplate> Templates { get; } = new[]
    {
        ViewTemplate.For<AutomationPageViewModel, AutomationPage>(),
        ViewTemplate.For<AutomationItem, AutomationRuleRow>(),
    };
}
