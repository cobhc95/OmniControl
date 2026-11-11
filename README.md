# OmniControl

A Windows control panel for monitors and sound, built with Avalonia (.NET 8).

- **Displays**: brightness, contrast and more over DDC/CI (VESA MCCS), plus laptop backlight.
- **Volume**: master, per-program volume, pan, mute and output device; recording devices (inputs); live level meters; FX chains per program, master and input (stored settings, no audio processing yet).
- **OSD**: custom on-screen pop-up for volume and brightness; can hide Windows' own.
- **Shortcuts**: every action is a rebindable global hotkey.
- **App settings**: theme (System/Light/Dark), accent colour, start with Windows, tray behaviour.

## Build and run

Needs the .NET 8 SDK on Windows.

```powershell
dotnet run --project OmniControl.App -c Release
```

Monitors need DDC/CI enabled in their own on-screen menu.

## For contributors and agents

Start with [AGENTS.md](AGENTS.md) (rules), [ARCHITECTURE.md](ARCHITECTURE.md) (code map) and [HANDOVER.md](HANDOVER.md) (what exists and how it is wired).
