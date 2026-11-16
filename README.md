# OmniControl

A Windows control panel for monitors and sound, built with Avalonia (.NET 9). Free software under GPL-3.0-or-later (see LICENSE and NOTICE.txt).

- **Displays**: brightness, contrast and more over DDC/CI (VESA MCCS), plus laptop backlight.
- **Volume**: master, per-program volume, pan, mute and output device; recording devices (inputs); live level meters; FX chains per program, master and microphone, run by the effects engine (through VB-CABLE and OmniControl's own engine, or the master in the user's own Equalizer APO; EQ, gain and VST3 and VST2 plug-ins, VST2 also in Equalizer APO; extra plug-in folders in App settings).
- **Mouse and Trackpad, and Gestures**: remap mouse buttons and precision-touchpad gestures (three- and four-finger swipes, pinch) in layers, wheel and middle-click gestures over screen areas, and custom rules ("if ... then ...").
- **OSD**: custom on-screen pop-up for volume and brightness; can hide Windows' own.
- **Shortcuts**: every action is a rebindable global hotkey.
- **App settings**: theme (System/Light/Dark), accent colour, start with Windows, tray behaviour.

## Build and run

Needs the .NET 9 SDK on Windows. The optional master effects engine (OmniControl APO) is C++ and is built separately: see native/EqualizerAPO/README.md.

```powershell
dotnet run --project OmniControl.App -c Release
```

Monitors need DDC/CI enabled in their own on-screen menu.

## For contributors and agents

Start with [AGENTS.md](AGENTS.md) (rules), [ARCHITECTURE.md](ARCHITECTURE.md) (code map) and [HANDOVER.md](HANDOVER.md) (what exists and how it is wired).
