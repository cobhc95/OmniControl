# OmniControl — DDC/CI engine (Avalonia, C#)

Real hardware monitor control via **VESA MCCS VCP over DDC/CI** (Windows `dxva2.dll`), not gamma overlays.

## Build & run (per cleanup rule: always stop → build → launch)

```powershell
Stop-Process -Name OmniControl -Force -ErrorAction SilentlyContinue
dotnet build OmniControl.slnx -c Release -f net8.0-windows
dotnet run --project OmniControl.App -c Release -f net8.0-windows
# Linux (stub DDC/CI): dotnet build OmniControl.slnx -c Release -f net8.0
```

Requires .NET 8 + DDC/CI monitors (enable in OSD). Laptop uses WMI/sysfs.

## Layout (small + separated from `git init`)

```
OmniControl.Core/          net8.0;net8.0-windows — engine only
  Interfaces/           IMonitor, IMonitorEngine, ILaptop, ISystemVolume
  Models/               VcpCode, MonitorInfo, VcpValue
  Native/               Dxva2Native (dxva2/user32 P/Invoke only)
  Engine/               DdcCiMonitor, MonitorEnumerator, backlight/volume/rotation/GPU
  Command/              CommandLanguage (single funnel API)
  MonitorEngine.cs      facade, injectable (IMonitorEnumerator/ILaptop/…)
OmniControl.App/      net8.0;net8.0-windows — UI only, MVVM
  ViewModels/           MainViewModel (injects IMonitorEngine, disposes it)
  Views/                MainWindow.axaml(+cs)
.gitignore / .editorconfig / .gitattributes / Directory.Build.props
```

- WinForms `OmniControl.App/` removed — one UI only (Avalonia), no duplicated logic.

## Command language

`<monitor> <action> <value> [ … ]` e.g. `PL2779Q b 36`, `ALL b +2`, `LCD b 70`, `SYS v 30`, `GPU sat 50`. See `OmniControl.Core/Command/CommandLanguage.cs`.

## Works today

- ✅ Brightness/Contrast/Sharpness/RGB gain/Colour temp/Input/Volume/Mute/Power/Reset/OSD (VCP 0x10…0xD6, range read per panel)
- ✅ Laptop LCD (WMI Windows, sysfs Linux), system volume (Core Audio), rotation (DEVMODE)
- ⚠️ GPU saturation NVAPI/ADL stub — vendor SDK needed. Linux DDC/CI (`/dev/i2c`) stub.

Next: tray icon, hotkeys, profiles, ambient sync, idle dim.
