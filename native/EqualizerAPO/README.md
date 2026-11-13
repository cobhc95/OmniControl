# OmniControl APO (fork of Equalizer APO)

The master effects engine: an Audio Processing Object that Windows' audio engine (audiodg) runs on the whole mix of a playback
device. OmniControl writes the master effect chain as Equalizer APO config text (`config.txt` in `C:\ProgramData\OmniControl\APO`)
and the APO reloads it when the file changes.

## Origin and licence

- Upstream: Equalizer APO, https://sourceforge.net/projects/equalizerapo/ (git `https://git.code.sf.net/p/equalizerapo/code`),
  commit `bbfcc3e5024cbb9d61ba75fc88d78605cc4c9687` (2025-11-28, version 1.4.2). Copyright (C) 2012-2025 Jonas Thedering and
  contributors. Licence: GNU GPL version 2 or (at your option) any later version (`License.txt`, unchanged, and the header of
  every source file). OmniControl as a whole is distributed under GPL-3.0-or-later (repository `LICENSE`).
- Bundled upstream parts kept unchanged with their own notices: `libHybridConv-0.1.1/` (Christian Borss, LGPL-2.0-or-later),
  `helpers/aeffectx.h` (VeSTige, LMMS, GPL-2.0-or-later).
- Left out (GUI and tools, Qt): `Editor/`, `DeviceSelector/`, `Benchmark/`, `UpdateChecker/`, `VoicemeeterClient/`, `Setup/`,
  `Wiki/`, the upstream solution and batch files. The Device Selector is replaced by OmniControl's own command-line setup mode
  (`OmniControl.exe --engine-setup ...`, C#, with a preview mode; `OmniControl.App/Audio/Engine/EngineSetupCommand.cs`).

## What was changed (GPL "prominent notice"; each changed source file says so under its copyright line)

| File | Change |
|---|---|
| `helpers/RegistryHelper.h` | Own COM class ids (pre-mix `{27AB1524-86BE-4216-B86B-EC533B2B90B6}`, post-mix `{7F3F0D64-5AAB-432D-943C-C0B715C12DA0}`) and own registry key `HKLM\SOFTWARE\OmniControlAPO` (and HKCU), so it never collides with an installed Equalizer APO |
| `EqualizerAPO/DllMain.cpp`, `EqualizerAPO/EqualizerAPO.cpp` | COM class and APO names "OmniControl APO" |
| `helpers/LogHelper.cpp` | Log file `%TEMP%\OmniControlAPO.log` |
| `EqualizerAPO/EqualizerAPO64.rc` | File and product names |
| `EqualizerAPO/EqualizerAPO.vcxproj` | Output name `OmniControlAPO.dll`; x64 library paths read from `$(OcDeps)` |
| `Common.vcxproj` | Include paths read from `$(OcDeps)` |
| `Directory.Build.props` (new) | Defines `OcDeps` (default `deps\` here) |
| `build.ps1` (new) | Builds x64 Release (see below) |

The ids in `OmniControl.Core/Engine/Apo/ApoIds.cs` must match `RegistryHelper.h`.

## Build (owner, on a PC with Visual Studio)

Requirements: Visual Studio 2022 or Build Tools 2022 with "Desktop development with C++" (MSVC v143), Windows 11 SDK 10.0.26100
(it contains `audioeng.lib`, `AudioBaseProcessingObject.lib`, `audiomediatypecrt.lib`, `wmcodecdspuuid.lib`), internet access.

```powershell
powershell -ExecutionPolicy Bypass -File native\EqualizerAPO\build.ps1
```

It downloads the dependencies into `native\EqualizerAPO\deps\` (git-ignored), checks their SHA-256, builds `Common.lib` and
`OmniControlAPO.dll` (x64 Release) into `build-x64\`, and copies `OmniControlAPO.dll`, `sndfile.dll` and `libfftw3f-3.dll` into
`OmniControl.App\apo\`, from where the app build copies them next to `OmniControl.exe`. It installs and registers nothing.
Never commit the built DLLs; a release must offer this exact source with them.

## Dependencies (pinned)

| Library | Version | Licence | Source | SHA-256 of the download |
|---|---|---|---|---|
| libsndfile | 1.2.2 (official win64 build) | LGPL-2.1-or-later | https://github.com/libsndfile/libsndfile/releases/download/1.2.2/libsndfile-1.2.2-win64.zip | `2173935C0C1ED13CF627951D34483F9D405EAD2EB473190461C42BA220643A3F` |
| FFTW | 3.3.5 (official 64-bit DLLs) | GPL-2.0-or-later | https://fftw.org/pub/fftw/fftw-3.3.5-dll64.zip | `CFD88DC0E8D7001115EA79E069A2C695D52C8947F5B4F3B7AC54A192756F439F` |
| muparserx | 3.0.1 exactly (3.0.2 removed a feature the config parser needs; only Equalizer APO's developer page still has it) | BSD-2-Clause | https://sourceforge.net/p/equalizerapo/wiki/Developer%20documentation/attachment/muparserx_v3_0_1_vs_2019.zip | `05627A52DE2D0439953FA54A1F025D250E9E9E18F13B1CB9220F25F5BBE1DE15` |
| Windows SDK | 10.0.26100 | Microsoft SDK licence | Visual Studio Installer | - |

The libsndfile zip carries no licence file: ship its `COPYING` from the 1.2.2 source tag with a release. FFTW's zip includes
`COPYING`. Keep every licence file next to the binary it belongs to.

## Install, test, remove

OmniControl APO is the optional, advanced route for a PC without Equalizer APO: when the user's own Equalizer APO is installed,
OmniControl uses it and this fork is not needed. Through OmniControl (Volume page, master row, mode button, "Equalizer APO" card;
offered only when the DLL was built and copied next to the app and no Equalizer APO is installed): **Preview changes** (changes
nothing), **Advanced install** (a short confirmation, then the administrator prompt starts OmniControl.exe itself with
`--engine-setup install`), **Remove** (puts every saved value back). The setup mode can also be run by hand from the app folder:
`OmniControl.exe --engine-setup preview-install --device <endpoint id> --source <folder> --log <file>` or
`OmniControl.exe --engine-setup preview-uninstall --log <file>` write the plan to the log without changing anything.

The APO is not signed by Microsoft; it loads only because the install sets `DisableProtectedAudioDG = 1` (see HANDOVER.md for
the risks and the test plan).
