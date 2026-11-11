# Handover

State of OmniControl for the next agent. Rules: `AGENTS.md`. File map: `ARCHITECTURE.md`. This file: what exists and how it is wired.

## What exists

| Page | What it does |
|---|---|
| Displays | Per-monitor brightness/contrast etc. over DDC/CI; laptop backlight. |
| Volume | Collapsible **Outputs** (master + programs: volume, pan, mute, output device, FX button + power) and **Inputs** (recording devices: level, mute, FX). Vertical live level meter at the end of every row (collapsed groups are not metered). |
| OSD | Custom pop-up for volume/brightness; options; hide Windows' own OSD. Show OSD + Hide Windows' OSD are half-width cards. |
| Shortcuts | One card per registered hotkey action; rebind, pick devices. |
| App settings | Theme and accent gallery cards, start with Windows, start minimised, tray on close (half-width pairs). |

Also: FX chain window (per program/master/input, Reaper-style), Add effect window (built-ins + VST3 file scan), rubber-band selection, drag-reorder, tray icon, orange multi-size icon (`Assets/OmniControl.ico`), wrap-around Up/Down navigation.

## How it is wired

```
Program.cs -> App.axaml.cs   builds services, ShellViewModel, MainWindow, tray
ShellViewModel               list of PageViewModels (one line each) + search + footer
App.axaml                    one DataTemplate per VM/card type -> its view
Page VM (Pages/<Name>)       owns cards (SliderItem/ChoiceItem/ToggleItem/GalleryItem/...), talks to a service
Service (App/Audio, Osd, Hotkeys, Services)   state, timers, persistence under %APPDATA%\OmniControl
Core (OmniControl.Core)      Windows only: DDC/CI (dxva2), Core Audio COM, WMI. No UI, no settings.
```

Data flow, volume example: `VolumePageViewModel` (40 ms `Timing.MeterFrame` timer) -> `MixerService.Peaks` / `CoreAudio.DevicePeaks` -> `MeterModel.Feed` -> `LevelMeter` repaints on `Changed`. Slider -> `MixerItem.VolumeChanged` -> `MixerService` -> `AudioSession`/`CoreAudio`.

Keyboard: `RowEditing` (inside a card list), `GroupedCards` (pages with collapsible groups), `MainWindow.axaml.cs` (sidebar). All wrap. `HalfPanel` lays out half cards in list order.

Persistence (all in `%APPDATA%\OmniControl`): `settings.json` (AppSettings), `mixer.json` (MixerSettings incl. FX chains; input chains keyed `input:<endpointId>`), `hotkeys.json`, `plugins.json`, OSD settings, `osd-log.txt` (diagnostics).

Adding things: **page** = VM + view + one line in `ShellViewModel` + one DataTemplate; **hotkey** = `HotkeyRegistry.Register`; **timing value** = `Timing.cs`; **half card** = `Half = true`.

## Known limits / open items

- FX chains are settings only: no audio engine, so nothing is processed. VST3 are listed and stored, never loaded. Porting TabForge's DSP and isolated VST3 host (github.com/cobhc95/TabForge, MIT, same owner) is the obvious next big feature.
- Meter ballistics (floor -60 dB, warm -12 dB, hold 1.2 s, fall 28 dB/s) are my own; TabForge's `MasterMeterModel` was not compared.
- Half-width cards pair only when the list is at least ~500 px wide (`HalfPanel.MinPairWidth`); at the default window size they may stack. Tune if wanted.
- Pan needs Windows to allow per-channel levels on a program's session (`AudioSession.CanPan`).
- Per-app output device uses an undocumented interface (`CoreAudio.Routing.cs`).
- Hiding Windows' OSD is best-effort.
- Recent UI work (inputs group, meters, input FX, theme gallery, half cards, accent tint, icon) was built and launched but not confirmed on screen by the owner.
- No automated tests exist; the owner tests by hand (rule 7).
