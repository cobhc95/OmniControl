using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Osd;
using OmniControl.App.Features.States;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using IPresetGroups = OmniControl.App.Shared.Presets.IPresetGroups;

namespace OmniControl.App.Features.Volume.Pages;

/// What the Volume page is made from: the services it shows and drives, and the callbacks into the rest of the app (made by the Volume module).
/// <param name="OpenFx">Opens the FX window for a chain key (null = master): the Easy view's effects.</param>
/// <param name="ToggleFx">Opens the FX window for a chain key, or closes it when it is open: a Mixer row's FX button.</param>
/// <param name="Ask">Shows a setup message window; true when the user confirmed.</param>
/// <param name="Combine">Shows the Combine devices window; true when the user pressed Create.</param>
/// <param name="PresetGroups">The master list of preset groups (the Volume presets register there).</param>
/// <param name="OpenPresets">Opens the preset manager at a section and group (the preset picker's "Manage presets…").</param>
/// <param name="States">The master list of states: the master's volume and mute and the default speaker are registered there.</param>
public sealed record VolumePageServices(MixerService Mixer, EffectsSupportService Support, EffectsEngine Engine, EngineInstaller Installer, IActionRegistry Hotkeys,
    IOsd Osd, Action<string> Report, Action<string?> OpenFx, Action<string?> ToggleFx, Func<DialogMessage, Task<bool>> Ask,
    Func<CombineDeviceViewModel, Task<bool>> Combine, VolumePresets Presets, IPresetGroups PresetGroups, Action<string?, string?> OpenPresets,
    IStateRegistry States, VolumePageSettings Settings);
