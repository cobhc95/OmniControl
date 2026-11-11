# OmniControl code map

Read this first. It says where each concern lives, so a change or a bug can be found without reading everything.
Keep it current: when you add or move a file, update the matching line.

## Layers (dependencies point down only)

```
OmniControl.App/Pages, Shell      views + view-models (what the user sees and does)
OmniControl.App/Osd, Audio,       app services (state, timers, persistence, OSD, mixer)
  Hotkeys, Services
OmniControl.Core                  Windows access only (DDC/CI, Core Audio, WMI). No UI, no Avalonia, no settings files.
```

- Core never references App. Services never reference Pages. Pages reference services, never each other. Exception: `App/Fx` (the FX windows) is a feature module and reuses the shared card types from `Pages/`; nothing in `Pages` depends on it except through `FxWindowService`.
- A view-model never calls Win32 or COM directly: it calls a service or Core.
- Views (`.axaml`) hold layout only; logic goes in the view-model or a service. Code-behind is for wiring input events.

## Where things are

| Concern | Files |
|---|---|
| Windows audio (devices, sessions, master, per-app routing, COM) | `Core/Engine/Audio/CoreAudio.*.cs`, `AudioSession.cs`, `AudioDevice.cs`. Interop only in `CoreAudio.Interop.cs`. |
| Monitors (DDC/CI, laptop brightness) | `Core/Engine/DdcCiMonitor.cs`, `MonitorEnumerator.cs`, `LaptopBrightnessController.cs`, `Core/MonitorEngine.cs` |
| Master volume interface | `Core/Engine/SystemVolumeController.cs` (thin wrapper over CoreAudio) |
| Mixer state: programs, remembered volume/device/EQ, re-applying on reopen | `App/Audio/MixerService.cs`, `MixerSettings.cs` (json), `ProcessIcons.cs` |
| FX chain data (slots, built-in effect catalog; chains of programs, the master and recording devices, the last under the key `input:`+endpoint id) | `App/Audio/FxModel.cs` (no UI); chains and on/off live in `MixerSettings` and are read/written through `MixerService` |
| Equaliser effect: 10-band parametric maths and presets | `App/Audio/Eq.cs` (no UI) |
| FX chain window (Reaper-style: chain list left, selected effect's editor right) | `App/Fx/`: `FxWindowService.cs` (opens one window per target), `FxWindow.axaml`, `FxChainView.axaml`, `FxChainViewModel.cs` (chain edits, output device), `FxSlotViewModel.cs` (one effect and its editor cards), `EqPanelItem.cs` / `EqGraph.cs` / `EqPanelRow.axaml` (the equaliser editor) |
| Add effect window (search built-ins and VST3, scan common folders, load a VST3 file) | `App/Fx/Plugins/`: `AddEffectWindow.axaml(.cs)` + `AddEffectViewModel.cs`, `PluginLibrary.cs` (list + `plugins.json`), `PluginScanner.cs` (file names only, never loads a plug-in), `PluginFolders.cs`, `PluginEntry.cs` |
| Drag to reorder a list | `App/Fx/ListDragReorder.cs` (render-transform only; list changes once on drop) |
| IR Loader file card | `App/Fx/IrFileItem.cs`, `IrFileRow.axaml(.cs)` |
| Volume page: collapsible Outputs (master + programs: volume, pan, mute, FX power) and Inputs (recording devices: level, mute) groups | `Pages/Volume/VolumePageViewModel.cs`, `VolumeGroup.cs`, `MixerItem.cs`/`MixerRow.axaml`, `InputItem.cs`/`InputRow.axaml`; pan maths in `Core/.../AudioSession.cs` (`PanMath`); keys via `Pages/GroupedCards.cs` |
| Level meters (L/R bars, peak hold, clip mark; design from TabForge's track meter) | drawing: `Pages/Volume/LevelMeter.cs`; numbers and ballistics: `App/Audio/MeterModel.cs`; peaks: `Core/.../CoreAudio.Meters.cs`, `AudioSession.Peaks`, `MixerService.Peaks`. The page's timer feeds only open groups, only while the page is shown |
| Recording devices (list, level, mute) | `Core/Engine/Audio/CoreAudio.Capture.cs` |
| Rubber-band (mouse rectangle) selection in a list | `App/Fx/RubberBandSelect.cs` |
| OSD pop-up | `App/Osd/OsdService.cs` (when to show), `OsdWindow.cs` (the window), `OsdView.cs` (drawing), `OsdSettings.cs` + `OsdOptions.cs` (settings and choices), `WindowsOsd.cs` (hide Windows' own), `BrightnessWatcher.cs`, `VolumeWatcher.cs` (outside changes) |
| Hotkeys | `App/Hotkeys/HotkeyRegistry.cs` (all actions), `HotkeyAction.cs` (one action), `HotkeyService.cs` (Windows registration), `HotkeyCapture.cs` (key presses in a hotkey box) |
| Program-wide services | `App/Services/`: `AppSettings` (settings.json), `AccentService` (the one accent colour), `AppLifecycle` (show/quit events), `StartupRegistration`, `TrayService` |
| Reading/writing the json files in %APPDATA%OmniControl (one helper; every settings class uses it) | `App/Services/AppDataStore.cs` |
| Intervals and grace periods (all of them) | `App/Services/Timing.cs` |
| Window lifetime (tray, minimise, quit) | `App/Shell/WindowLifecycle.cs`; start-up in `App/App.axaml.cs` |
| Shell: sidebar, search, footer | `App/Shell/ShellViewModel.cs`, `MainWindow.axaml(.cs)` |
| One page | `App/Pages/<Name>/<Name>PageViewModel.cs` + `<Name>Page.axaml(.cs)`; registered by one line in `ShellViewModel` and one DataTemplate in `App.axaml` |
| Half-width cards (two per row; reading order = list order; full width when narrow) | `App/Pages/HalfPanel.cs` (items panel of a card list), `IHalfWidth.cs` (a card opts in with `Half`; `ToggleItem` does) |
| Shared card types | `App/Pages/`: `SliderItem`, `ChoiceItem`, `ToggleItem`, `GalleryItem` (each with a `*Row.axaml`), `IEditorItem` (what search lists) |
| Keyboard model | `Pages/RowEditing.cs` (inside a card list), `Pages/GroupedCards.cs` (pages with collapsible groups) |
| Collapsible groups | `Pages/CardGroup.cs`, `GroupHeader.axaml` |
| Styles | `App/Styles/Theme.axaml` (no accent colours; those come from `AccentService`) |

## Rules that keep it separated

1. One concern per file, and no file over 700 lines. Split by concern (a `partial` class is fine) before it grows past that.
2. Settings files: one class owns one json file (`AppSettings`, `OsdSettings`, `MixerSettings`, `hotkeys.json` via the registry).
3. Search lists any `IEditorItem` with a template in `App.axaml`. Do not teach search about concrete card types.
4. A new feature page adds hotkeys through `HotkeyRegistry` (see AGENTS.md rule 4).
5. Behaviour that depends on a Windows quirk gets a comment saying which quirk, next to the code that works around it.

## Known limits (not bugs to chase)

- FX chains store settings only; no sound processing exists yet (Windows has no per-app effect API). VST3 plug-ins can be found, listed and put in a chain (saved as kind `vst`), but nothing loads or runs them. The built-in effect parameter lists follow TabForge (MIT, same author); its DSP, IR convolution and the isolated VST3 host are not ported yet because they need an audio engine first.
- Pan relies on Windows allowing per-channel levels for a program's session; if it doesn't, the pan slider has no effect (`AudioSession.CanPan`).
- Per-app output device uses an undocumented Windows interface (`CoreAudio.Routing.cs`), unverified against a reference.
- Hiding Windows' own OSD is best-effort (`WindowsOsd.cs`; diagnostics in `%APPDATA%\OmniControl\osd-log.txt`).
