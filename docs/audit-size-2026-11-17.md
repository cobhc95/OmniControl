# Download size audit (17 Nov 2026)

Read-only audit of the Setup (`tools/setup.iss`) and the Portable zip (`tools/release.ps1`). No project file was changed.
Every number below was measured by trial publishes of a copy of the source in `%TEMP%` (deleted afterwards), with the
same commands `release.ps1` uses (`dotnet publish -c Release -r win-x64 --self-contained`, ISCC with the real
`setup.iss`, `Compress-Archive`). Sizes are decimal MB (as GitHub shows them). The app was not run: nothing below is
checked at run time (rule 7); every "verify" line is for the owner.

Baseline reproduces 0.1.4: **Setup 52.3 MB, zip 67.0 MB** (151 MB, 238 files on disk).

## 1. What takes the space (baseline publish, on disk)

| Group | On disk | Files | Notes |
|---|---:|---:|---|
| .NET runtime, managed class libraries | 64.3 MB | 178 | `System.Private.CoreLib` 15.3, `System.Private.Xml` 7.9, `System.Linq.Expressions` 3.7, `System.Data.Common` 2.8, ... Almost all unused by the app. Pre-compiled (ReadyToRun), which is why they are big. |
| Avalonia + Skia + HarfBuzz + ANGLE | 26.1 MB | 28 | `libSkiaSharp` 9.4, `av_libglesv2` (ANGLE, GPU drawing) 5.4, `Avalonia.Base` 2.1, `Avalonia.Fonts.Inter` 1.9, `libHarfBuzzSharp` 1.8, `Avalonia.Controls` 1.1, `Avalonia.Win32` 0.9, Fluent theme 0.6; Linux/macOS parts that Windows never loads: `Avalonia.X11` 0.34, `Avalonia.Native` 0.30, `Avalonia.FreeDesktop` 0.20, `Tmds.DBus.Protocol` 0.20 |
| Windows SDK projection (CsWinRT) | 25.4 MB | 2 | `Microsoft.Windows.SDK.NET.dll` 24.9 MB, pulled in by the `net9.0-windows10.0.19041.0` target. The only WinRT code is `Core/Engine/Media/MediaWatcher.cs` (`Windows.Media.Control`). |
| Equalizer APO installer (`third_party`) | 12.0 MB | 1 | `EqualizerAPO-x64-1.4.2.exe`, already compressed: costs ~12 MB in both downloads. VB-CABLE is not bundled (only its README). `apo\` (OmniControl APO) was not present in this checkout. |
| Runtime native host, CLR, JIT, GC | 9.9 MB | 9 | `coreclr` 4.8, `clrjit` 1.9, `clrgc` + `clrgcexp` 1.4, `msquic` 0.5 (HTTP/3, unused) |
| Debugger and crash-dump natives | 6.4 MB | 5 | `Microsoft.DiaSymReader.Native.amd64` 2.4, `mscordaccore.dll` 1.36 **and an identical copy** `mscordaccore_amd64_amd64_9.0.*.dll` 1.36, `mscordbi` 1.2, `createdump.exe` 0.07. Only used to attach a debugger or write a crash dump. |
| OmniControl's own | 4.1 MB | 5 | `OmniControl.dll` 3.3 (compiled XAML and the icon drawings), `OmniControl.Core.dll` 0.6, exe 0.17 |
| PDB (symbols) | 1.2 MB | 2 | Give line numbers in crash logs and "Copy details". |
| NAudio | 1.1 MB | 4 | Wasapi, Core, Vst3, Midi (Midi comes along, unused) |
| ZXing | 0.5 MB | 1 | QR reading (`Core/Engine/Screenshot/QrReader.cs`), used |
| CommunityToolkit.Mvvm, texts | 0.2 MB | 3 | |
| Satellite language folders | 0 | 0 | None in the output (no package ships any for win-x64). |
| XML doc files | 0 | 0 | None. |
| Skia/HarfBuzz natives for other platforms | 0 | 0 | Already left out by `-r win-x64`. |
| Icons and assets | 13 KB | 1 | `Assets/OmniControl.ico` only; the 100 library icons are path data inside `OmniControl.dll`. |

## 2. Options, each measured

Setup / zip sizes are shown **with the Equalizer APO installer / without it**.

| # | Option | Setup | Zip | Saving vs 52.3 / 67.0 | Risk |
|---|---|---:|---:|---|---|
| 0 | Baseline (today) | 52.3 / 40.3 | 67.0 / 55.0 | | |
| A | Equalizer APO installer not bundled (download on demand) | 40.3 | 55.0 | -12.0 / -12.0 | Low (see below) |
| B | No PDB (`DebugType=None`) | 51.8 / 39.8 | 66.3 / 54.4 | -0.5 / -0.6 | Low; crash logs lose line numbers |
| C | Trim only `Microsoft.Windows.SDK.NET` + `WinRT.Runtime` (rest copied unchanged) | 48.3 / 36.3 | 60.2 / 48.3 | -4.0 / -6.7 | Low-medium: media watcher only |
| D | C + no debugger natives + no X11/macOS Avalonia parts ("lean", simulated by deleting the files) | - / 34.5 | - / 45.1 | -5.8 / -9.9 vs A | Low |
| E | Framework-dependent (needs .NET 9 runtime installed), no PDB | 27.5 / 15.0 | 32.2 / 19.6 | -24.8 / -34.8 (with the installer) | Medium: runtime must be present |
| F | Partial trim (runtime, Avalonia, libraries trimmed; OmniControl's own assemblies untouched) + COM and JSON switches kept on | 30.1 / 18.1 | 35.4 / 23.4 | -22.2 / -31.6 (with the installer) | Medium-high, start-up slower (see 2.3) |
| F2 | F + ReadyToRun (to win back start-up speed) | - / 29.4 | not measured | -10.9 vs A | Medium-high |
| G | Full trim | 29.4 / 17.4 | 34.5 / 22.5 | | High: does not work today without code changes |
| H | Single file + compression (untrimmed) | 62.6 | 62.2 | Setup +10, zip -5 | **Broken as is** (see 2.2) |
| H2 | C + single file + compression | 57.0 | 56.3 | | Same |
| I | Inno `lzma2/ultra64`, 128 MB dictionary, `LZMANumFastBytes=273`, `LZMAUseSeparateProcess=yes` | -0.6 | | -0.6 | None (a 256 MB dictionary fails: LZMA error 5, out of memory) |
| J | Zip at maximum deflate level | | -0.15 | -0.15 | None, not worth it |
| K | ReadyToRun on the whole app, untrimmed | 198 MB on disk | | +47 MB on disk | Bigger, not smaller |

### 2.1 Framework-dependent vs self-contained (E)
- Drops the 80 MB of runtime (BCL, CLR, debugger natives). Setup 15.0 MB, zip 19.6 MB without the Equalizer APO installer.
- The app needs only `Microsoft.NETCore.App` (no WindowsDesktop runtime: no WPF or WinForms).
- **Bootstrapper:** in `setup.iss`, `[Code]` checks `HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App` for a 9.0.x value; if missing, download the official `dotnet-runtime-9.0.x-win-x64.exe` (about 28 MB) from Microsoft with Inno 6's `DownloadTemporaryFile` (fixed version and SHA-512 from Microsoft's release JSON) and run it with `/install /quiet /norestart`. That installer needs administrator rights, so a UAC prompt appears only for users who lack the runtime; the Setup itself stays `PrivilegesRequired=lowest`.
- **Portable zip:** without the runtime, Windows' host shows its own "install .NET" dialog with a link. Either ship two zips (Portable, and Portable self-contained) or keep the zip self-contained using the lean options below.
- **Important:** .NET 9 is a short-term release; Microsoft's extended STS policy ends its support on 10 Nov 2026 (please verify), so it is already past. Making users install a .NET 9 runtime now is a poor choice; do this together with a move to .NET 10 (LTS), which is a separate job.
- Verify: on a clean Windows VM without .NET, run Setup (runtime gets installed, app starts), run the zip (dialog appears).

### 2.2 Single file + compression (H, H2): not recommended
- The Setup gets **bigger** (57-63 MB): LZMA cannot compress an already compressed exe, and the solid archive loses its cross-file gains.
- It is broken as configured: the `None` items (`third_party\...`, `apo\*.dll`) are bundled **inside** the exe (the exe was 68 MB), so `EngineInstaller` (which looks in `AppContext.BaseDirectory\third_party`) and `ForkBuilt` (`apo\`) would find nothing. Would need `ExcludeFromSingleFile="true"` on those items.
- Native libraries (Skia, ANGLE, HarfBuzz) extract to a temp folder on first start; slower first start and a common antivirus trigger.

### 2.3 Trimming
Measured with `PublishTrimmed=true`. Trimming switches several runtime features **off by default**, which would break the app silently:
- `BuiltInComInterop.IsSupported=false`: every `[ComImport]` interface and `Activator.CreateInstance(Type.GetTypeFromCLSID(...))` stops working (Core Audio, policy config, UI Automation, touchpad settings, NAudio, WMI). Must set `<BuiltInComInteropSupport>true</BuiltInComInteropSupport>`.
- `JsonSerializer.IsReflectionEnabledByDefault=false`: every settings load and save throws. Must set `<JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>` or move to a `JsonSerializerContext`.
- The trimmed runtime assemblies lose their pre-compiled (ReadyToRun) code (`System.Private.CoreLib` 15.3 MB -> 3.4 MB), so the runtime is JIT-compiled at start: slower start-up. F2 (adding `PublishReadyToRun`) restores it but costs back about 11 MB of the Setup saving.

Variants:
- **C: trim only the Windows SDK projection.** A small target sets `IsTrimmable=false` / `TrimMode=copy` on every `ManagedAssemblyToLink` except `Microsoft.Windows.SDK.NET` and `WinRT.Runtime`, so the runtime, Avalonia and the app are copied unchanged (and keep ReadyToRun). The projection shrinks from 24.9 MB to 0.14 MB (0.27 MB with a keep-list). CsWinRT finds ABI helper types by name at run time, so root what MediaWatcher uses with an ILLink descriptor (`TrimmerRootDescriptor` item) that preserves the namespaces `Windows.Media.Control`, `Windows.Media`, `Windows.Foundation`, `Windows.Foundation.Collections`, `Windows.Storage.Streams` and their `ABI.*` twins, plus `WinRT.Runtime` whole. Verify: media OSD and "now playing" (MediaWatcher: session list, play/pause changes, title/artist) with Spotify or a browser video; settings load and save; volume/mixer (COM); trackpad settings; UI Automation mouse features.
- **F: partial trim.** Trims everything marked trimmable (runtime, Avalonia, NAudio, SkiaSharp, CommunityToolkit, System.Management); OmniControl.dll and OmniControl.Core.dll are not trimmed. 1,599 trim warnings, 1,586 of them `ReflectionBindingExtension` in the XAML (compiled bindings are off). Bindings to OmniControl view-models are safe (not trimmed), but any XAML binding path through a CLR property of a runtime or Avalonia type (`Items.Count`, `.Length`, a `Tag` on a control...) may break at run time only when that view opens. Would need a full manual pass of every window. Not recommended until bindings are compiled.
- **G: full trim.** Same warnings, also trims the app; would need compiled bindings everywhere and JSON source generation first.
- Assemblies that would need `TrimmerRootAssembly` or a descriptor if F/G are ever done: `WinRT.Runtime` and the `Windows.Media.Control` part of `Microsoft.Windows.SDK.NET`; `System.Management` (WMI, produces IL2104); `Microsoft.CSharp` (the `dynamic` in InstalledPrograms); `Avalonia.Markup.Xaml` and `Avalonia.Base` (reflection bindings); `System.Text.Json` with the settings types (or source generation); `NAudio.Wasapi`/`NAudio.Core` (COM; the trimmer keeps `[ComImport]` interfaces whole, but check). How to test: publish with `-p:TrimmerSingleWarn=false`, read every IL2026/IL2072/IL2104, then open every page and window in both themes and exercise audio, devices, presets, backup/restore.

### 2.4 Other options
- **Satellite languages** (`SatelliteResourceLanguages=en`): nothing to remove today; adding it is a harmless guard for future packages. Saving 0.
- **PDB and XML docs:** no XML docs. PDBs cost 0.5 MB compressed; they put line numbers in crash logs. Suggest keeping them, or `DebugType=embedded` (same size, one file fewer). Optional: publish them as a separate release asset instead.
- **Debugger natives** (`DiaSymReader`, both `mscordaccore` copies, `mscordbi`, `createdump`): 6.4 MB on disk, about 1.5 MB Setup and 2.5 MB zip. Removing them (a post-publish `Remove-Item` in `release.ps1`, or an `ItemGroup` removing them from `ResolvedFileToPublish`) loses only debugger attach and crash dumps of a release build. Low risk; verify the app starts and a deliberate error still writes the log.
- **Unused Avalonia platforms:** replace `Avalonia.Desktop` with `Avalonia.Win32` + `Avalonia.Skia` and `.UsePlatformDetect()` with `.UseWin32().UseSkia()` in `Program.cs`: drops X11, macOS, FreeDesktop, DBus (1.0 MB on disk, about 0.4 MB compressed). Low risk (Windows only app). Verify start-up, tray, file pickers (`PathPicker`, still Win32).
- **ANGLE (`av_libglesv2`, 5.4 MB):** the GPU renderer. Removing it forces software rendering (more CPU on every redraw). Keep.
- **Inter font (`Avalonia.Fonts.Inter`, 1.9 MB):** it is used: `Program.cs` calls `.WithInterFont()`, so Inter is the app's default font. Removing it changes the look (owner's decision); not recommended.
- **Fluent theme:** used (base control templates). Keep.
- **Equalizer APO installer:** bundling is allowed (GPL v2+) only with its licence notice (NOTICE.txt has it) and an offer of its exact source. Downloading on demand from the official SourceForge page avoids the source-offer duty and saves 12 MB in each download. `EngineInstaller` already falls back to opening the download page when the installer is missing, so simply not placing it before a release works today with no code change (the user downloads it themselves). A nicer later step: download the pinned version (`EqualizerAPO-x64-1.4.2.exe`) to `%TEMP%`, check its SHA-256, then run it (a service in App with the HTTP code in Core; needs an owner-approved design for progress and failure wording).
- **VB-CABLE:** not bundled now; its licence allows bundling the package AS IS, but keeping it on demand (as today) keeps the downloads small. No change.
- **Inno Setup:** `Compression=lzma2/ultra64`, `LZMADictionarySize=131072`, `LZMANumFastBytes=273`, `LZMAUseSeparateProcess=yes`, `SolidCompression=yes` (already on): -0.6 MB. Free, safe. 256 MB dictionary fails.
- **Zip level:** `Compress-Archive` (Optimal) vs maximum deflate: -0.15 MB. Not worth it. A 7-Zip archive would be smaller but is not a zip.
- **Duplicates:** the two identical `mscordaccore` files (1.36 MB each; solid Setup stores it once, the zip twice); `NOTICE.txt` both embedded and loose (9 KB, intentional). No duplicates between the app folder and the bundled installers.
- **Icons/assets and dead assets:** one 13 KB `.ico`; `third_party/tabler-icons` holds only its LICENSE (needed for the icon credits). `Showcase\**` is already compiled out of Release. `NAudio.Midi` (0.06 MB) comes with NAudio and is unused; not worth a hack. Nothing else dead was found.

## 3. Recommended plan (safe wins first)

| Step | Change | Where | Setup | Zip |
|---|---|---|---:|---:|
| 0 | Today | | 52.3 | 67.0 |
| 1 | Stop bundling the Equalizer APO installer (the in-app button already opens the official page) | release routine; README in `third_party/equalizerapo` | 40.3 | 55.0 |
| 2 | Inno ultra64 + 128 MB dictionary | `tools/setup.iss` | 39.7 | 55.0 |
| 3 | Drop debugger natives; `Avalonia.Win32` + `Avalonia.Skia` instead of `Avalonia.Desktop` | `release.ps1` or csproj; `Program.cs` | ~38 | ~52 |
| 4 | Trim only the Windows SDK projection with a WinRT keep-list | csproj + one descriptor file | ~34 | ~45 |
| 5 (later, with .NET 10) | Framework-dependent Setup with the runtime bootstrapper; keep the zip self-contained (step 4 form) or offer both | `setup.iss` `[Code]`, `release.ps1` | ~15 | ~45 (or ~20 framework-dependent) |

Expected after steps 1-4: **Setup about 34 MB (-35 %), zip about 45 MB (-33 %)**, no feature lost, start-up unchanged.
After step 5: **Setup about 15 MB** (users without the runtime download about 28 MB once, from Microsoft).
Partial or full trimming (F/G, about 17-18 MB Setup self-contained) only after compiled bindings and JSON source generation; not worth the risk now.

## 4. Code that blocks trimming (for the code audit)

- **Reflection bindings everywhere:** compiled bindings are off (AGENTS.md "Bindings are resolved at run time"), 1,586 IL2026 warnings across nearly every view (most in `FxWindow`, `EasyFlowView`, `SetupView`, `AutomationPage`, `DeviceDetail`, `CaptureOverlay`, `VolumePage`), plus one code binding `new Binding(nameof(FxSlotViewModel.ScaleFactor...))` in `App/Fx/EffectEditorDock.cs:158`.
- **JSON by reflection, no `JsonSerializerContext`:** `Services/AppDataStore.cs`, `Services/Backup/BackupFile.cs`, `Services/Backup/PendingRestore.cs`, `Audio/AudioDefaults.cs`, `Audio/UserPresetStore.cs`, `Audio/MixerService.Cables.cs`, `Devices/DeviceService.Scripts.cs`, `Devices/DeviceSettings.cs`, `Devices/MethodRecord.cs`, `Mouse/MouseRestoreState.cs`, `Pages/Gestures/GestureRestoreState.cs`, `Pages/Restore/SettingsRestore.cs`.
- **`dynamic` late-bound COM:** `Core/Engine/InstalledPrograms.cs:94-95` (`WScript.Shell` `CreateShortcut(...).TargetPath`). Replace with a `[ComImport]` `IShellLinkW` + `IPersistFile`; this also removes the need for `Microsoft.CSharp` and `System.Linq.Expressions` at run time.
- **Built-in COM interop (`[ComImport]` + `Activator.CreateInstance(Type.GetTypeFromCLSID(...))`):** `Core/Engine/Audio/CoreAudio*.cs`, `Core/Engine/Mouse/UiaElements.cs`, `Core/Engine/Trackpad/ShellTouchpadSettings.cs`, `InstalledPrograms.cs` (IL2072). Works with `BuiltInComInteropSupport=true`; the trim- and AOT-safe route is `[GeneratedComInterface]` with `ComWrappers`.
- **WMI:** `Core/Engine/LaptopBrightnessController.cs`, `Core/Engine/UsbDeviceWatcher.cs` (`System.Management` gives IL2104).
- **WinRT:** `Core/Engine/Media/MediaWatcher.cs` is the only user of the 25 MB SDK projection.
- **Reflection by name:** only `Showcase/ShowcaseExporter.cs` (`GetProperty("IsEasy")`), Debug-only, not in Release.
- No `Type.GetType(string)`, `Assembly.Load`, runtime XAML loading (only the normal `AvaloniaXamlLoader.Load(this)` in `App.axaml.cs`, which is compiled) or `XmlSerializer` found.
