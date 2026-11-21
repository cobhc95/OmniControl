using OmniControl.App.Features.AppLists;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Icons;
using OmniControl.App.Features.Osd;
using OmniControl.App.Features.OtherPcs.SettingsImport;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features;

/// What every feature module is given: the master lists through their narrow interfaces and the shell's shared openers.
/// <param name="Settings">The app's settings, of which a module hands each feature only its own fields (<see cref="AppSettings.Slot"/>).</param>
/// <param name="Imports">Where a module says how an import reloads its stores live (<see cref="ImportHooks.OnReload"/>).</param>
/// <param name="Shutdown">Where a module adds its shut-down steps, each at its place in the one order (run alone when its feature is switched off).</param>
/// <param name="Features">Which features are on (a module with parts of its own, such as Network traffic control inside Resources, reads them when it starts).</param>
/// <param name="OpenPage">Opens the page with this title, at the tab with this name (<see cref="PageViewModel.Tabs"/>; null: the tab it is on).</param>
public sealed record FeatureContext(
    IActionRegistry Actions,
    IActionRunner Runner,
    IStateRegistry States,
    IHardwareWatches Hardware,
    IOsd Osd,
    IPresetGroups Presets,
    Action<string?, string?> OpenPresetManager,
    Func<DialogMessage, Task<bool>> AskAsync,
    Func<DialogMessage, Task<string?>> AskTextAsync,
    Action<string> Report,
    Action<string, string?> OpenPage,
    AppScopes AppScopes,
    AppScopeGate AppScopeGate,
    IconPicker IconPicker,
    ActionGateSlot ActionGate,
    IPowerEvents Power,
    Func<string, SuggestedKeysSwitch> SuggestedKeys,
    AppSettings Settings,
    ImportHooks Imports,
    ShutdownSteps Shutdown,
    FeatureSwitches Features);
