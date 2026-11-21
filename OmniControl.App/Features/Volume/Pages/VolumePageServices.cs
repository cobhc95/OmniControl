using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;

namespace OmniControl.App.Features.Volume.Pages;

/// What the Volume page is made from: the services it shows and drives, and the callbacks into the rest of the app (made by the Volume module).
/// <param name="Controls">The levels, Listen and master device the rows write through, with the page's actions and states (made at start-up).</param>
/// <param name="Apo">The Wiring tab's Equalizer APO group (the module's: the FX windows offer its step too).</param>
/// <param name="OpenFx">Opens the FX window for a chain key (null = master): the Easy view's effects.</param>
/// <param name="ToggleFx">Opens the FX window for a chain key, or closes it when it is open: a Mixer row's FX button.</param>
/// <param name="Ask">Shows a setup message window; true when the user confirmed.</param>
/// <param name="Combine">Shows the Combine devices window; true when the user pressed Create.</param>
/// <param name="OpenPresets">Opens the preset manager at a section and group (the preset picker's "Manage presets…").</param>
public sealed record VolumePageServices(MixerService Mixer, VolumeControls Controls, EqualizerApoGroup Apo, EffectsSupportService Support, EffectsEngine Engine,
    EngineInstaller Installer, Action<string> Report, Action<string?> OpenFx, Action<string?> ToggleFx, Func<DialogMessage, Task<bool>> Ask,
    Func<CombineDeviceViewModel, Task<bool>> Combine, Action<string?, string?> OpenPresets, VolumePageSettings Settings);
