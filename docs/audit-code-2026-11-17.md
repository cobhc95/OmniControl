# Code audit, 17 Nov 2026 (read-only)

Scope: `OmniControl.App`, `OmniControl.Core`, `tests` (1013 .cs/.axaml files, about 41,900 lines). Method: grep sweeps (word-frequency
table across every .cs and .axaml for dead names; `+=`/`-=` balance; catch shapes; Win32 imports; partial-class sizes), then reading the
hot spots. Nothing was built, run or changed. Line numbers are from the working tree of 17 Nov (uncommitted changes included).

Not verified: dead-name candidates were checked by name across all .cs and .axaml (including `XxxCommand` for `[RelayCommand]` and
`[ObservableProperty]` fields); a name used only through reflection, JSON or a string resource lookup could still be live, so grep once
more before deleting. Fault paths were reasoned from code, not provoked.

Severity: high = can crash the app, lose user data or leave Windows changed; medium = makes a bug in one feature spread or makes
debugging read many classes; low = tidiness.

---

## Top 15 by value

| # | Finding | Sev | Where |
|---|---------|-----|-------|
| 1 | No app-wide handler for unhandled exceptions: any throw in a UI event, `async void` handler or posted action ends the whole process | high | `App.axaml.cs:17`, `Program.cs:20` |
| 2 | The one run path has no catch: an action that throws (device, OSD, screenshot, script) crashes the app from a hotkey, tray tile, rule or gesture | high | `Hotkeys/HotkeyRegistry.cs:187-200` |
| 3 | Shutdown is one unguarded chain: one `Dispose` that throws skips putting audio routing, the pointer swap and the APO chain back | high | `Shell/ShellViewModel.cs:309-339` |
| 4 | Settings saves are not atomic and failures are silent; a damaged file loads as null and is then overwritten with defaults | high | `Services/AppDataStore.cs:27-50` |
| 5 | Start-up composes every service and page in one constructor with no guard: one failing page or service stops the app from opening | high | `Shell/ShellViewModel.cs:119-195` |
| 6 | `ThrottledRun` runs work on a thread-pool timer with no catch: a throw in the display rescan, effects support check or program discovery kills the process | high | `Services/ThrottledRun.cs:41-56` |
| 7 | Services hand out their mutable `Settings`; pages and other services edit them and then call `Apply`/`Save` themselves | medium | `GesturesPageViewModel.cs:89-101`, `Rules/RuleService.cs:76,120,131`, `DevicesPageViewModel.cs` (5), `EasyEnvironment.cs` (3) |
| 8 | "Post the report to the UI thread" is re-done by each service, and one path (the Volume page) wires a service's report | medium | `AutomationService.cs:65`, `GestureService.cs:37`, `MouseService.cs:367`, `RuleService.cs:49`, `HardwareDisplayItem.cs:199`, `VolumePageViewModel.cs:135` |
| 9 | `VolumePageViewModel` is 13 partial files, 1,938 lines and 15 constructor parameters; routing and wiring rules live in a page | medium | `Pages/Volume/VolumePageViewModel*.cs` |
| 10 | `HotkeyRegistry` holds five concerns (master list, sections, run path and gate, persistence, Windows binding) plus settable `Gate`/`Report` | medium | `Hotkeys/HotkeyRegistry.cs:19-326` |
| 11 | Static mutable state: `AppSettings.Current` (27 uses in 10 files, view-models included), `AccentService`, `ComboKeys`, `RowEditing`, `PickerWindow._keys` | medium | see section B4 |
| 12 | 16 bare `catch { }` blocks in CoreAudio, mixer and icons swallow every exception type, bugs included | medium | see section B8 |
| 13 | `Theme.axaml` is over the 700-line limit (756) | medium | `Styles/Theme.axaml` |
| 14 | Dead code: about 20 members, 2 unused commands that duplicate code-behind, 2 resources, 2 style classes | low | see section A |
| 15 | `RuleService` takes the concrete `GestureService` and `MouseService` and reads their `Settings` | medium | `Rules/RuleService.cs:41` |

---

## A. Dead code

All of these appear once in the whole repo (the declaration only). Fix: delete. Risk: very low; grep each name once more first.

| File:line | Member | Note |
|-----------|--------|------|
| `App/AppLists/AppListManagerViewModel.cs:147-151` | `[RelayCommand] AddRunning`, `AddInstalled` | `AddRunningCommand`/`AddInstalledCommand` are bound nowhere; `AppListManagerWindow.axaml.cs:86,92` does the same job in code-behind. Keep one: bind the commands in XAML and delete the click handlers (the view should not decide) |
| `App/AppLists/AppListStore.cs:89` | `Unregister` | Never called. Either call it when a scope model goes away, or delete it |
| `App/Devices/DeviceService.Scripts.cs:20` | `AllProfiles` | |
| `App/Devices/DeviceSettings.cs:37` | `FirstKind` | `[JsonIgnore]` computed property |
| `App/Devices/DeviceSetup.cs:33` | `PythonPage` | |
| `App/Screenshot/UndoList.cs:17` | `CanRedo` | |
| `App/Audio/Engine/EngineJournal.cs:71` | `HasLeftOver` | |
| `App/Audio/EffectsRules.cs:91` | `MicApoMissing` | |
| `App/Mouse/PointerSettingsService.cs:34` | `IsSwapOnTrial` | |
| `App/Mouse/PickChoice.cs:31` | `NeedsSettings` | |
| `App/Hotkeys/HotkeyRegistry.cs:49` | `RankOf` | |
| `App/Hotkeys/HotkeyRegistry.cs:136` | `MoveKey` | "Move here" is done some other way |
| `App/Display/SoftwareDisplayService.cs:127,130` | `SetSaturation`, `SetInvert` | |
| `App/Pages/CardGroup.cs:95` | `HasItems` | |
| `App/Pages/Devices/SimStepItem.cs:85` | `HasTarget` | |
| `Core/Engine/Apo/ApoStatus.cs:57,69,85` | `UpstreamOnDevice`, `UsesUpstream`, `ConfigTarget` | |
| `Core/Engine/Audio/CoreAudio.Master.cs:28` | `DefaultDeviceName()` | |
| `Core/Engine/Audio/AudioPipe.cs:105` | `CapturedPackets` | |
| `App/App.axaml:59` | resource `PathFieldWidth` | |
| `App/Styles/Brushes.axaml:87` | resource `PlusIcon` | AGENTS.md still names it as the tray's glyph, so the doc is stale too |
| `App/Styles/Theme.axaml:385` | `Button.usebtn.picked` selector | class `picked` is never set |
| `App/Styles/Theme.axaml:745` | `TextBlock.status` | class `status` is never set |

Clean: no unused types (only test classes appear once), no unused `Timing`/`CoreTiming` constants, no TODOs, no typed bindings
(`$parent[x:` or `(x:Type)`), and the duplicate resource keys are just the light/dark theme dictionaries.

---

## B. Entanglement

### B1. Large files and partial classes with several concerns

| Where | Size | Problem | Fix | Risk |
|-------|------|---------|-----|------|
| `Styles/Theme.axaml` | 756 lines | Over the 700-line rule; holds buttons, lists, cards, pills, text and more | Split by control family the way `Menus.axaml`, `Bars.axaml` and `Sliders.axaml` already are: move the `Button.*` masters to `Styles/Buttons.axaml` and the card/list styles to `Styles/Cards.axaml`, included in the same order. **Medium** | Low if the include order keeps the later-wins order (Avalonia has no specificity) |
| `Pages/Volume/VolumePageViewModel*.cs` | 13 files, 1,938 lines; ctor `VolumePageViewModel.cs:119` takes 15 args | Routing (`.Routing.cs` 234), Wiring (`.Wiring.cs` 361), Combine, Cables and Steps are routing/rules logic held by a page; a bug in Wiring means reading 13 files | Move the routing model (`TargetsOf`, what a Wiring choice does, cable steps) to an `Audio/Routing` service the page asks; keep the page for rows and menus. Bundle the ctor's callbacks (`openFx`, `toggleFx`, `ask`, `combine`, `openPresets`) into one small `IVolumeHost` interface. **Medium** | Medium: covered by `RoutePlannerTests`, `ProgramRoutingTests`, `MasterMethodTests` |
| `Devices/DeviceService*.cs` | 9 files, 1,056 lines | Actions, auto-checks, icons, methods, running, scripts, simulation and states in one service; the pages also edit `Settings` (B2) | Move `.Simulation.cs` (178) and `.Scripts.cs` (152) into their own services (`SimulationService`, `ScriptProfileStore`) that `DeviceService` uses. **Medium** | Medium |
| `Picker/PickerModel*.cs` | 4 files, 810 lines (`PickerModel.cs` 598) | Pick mode, manage mode (Actions and hotkeys) and live updates in one model | Leave as is until it nears 700; when it does, make manage mode its own model that wraps the picker. **Low** | Low |
| `Picker/PickerMenu.axaml.cs` | 317 lines, ~43 branches | Key routing (which pane gets Up/Down, what Esc does) is decided in code-behind | Move the key decisions to `PickerModel` (a `Decide(key, focus)` like `ComboKeys.Decide`) and keep the view to focus moves. **Low** | Low; `ActionPickerTests` covers the model |
| `Hotkeys/HotkeyRegistry.cs` | 327 lines | Master list, section ranking, run path with gate and chain, import/export, rebinding to `HotkeyService`; `Gate` (:60) and `Report` (:63) are settable after construction, so the order things are made in matters | Split out `ActionSections` (sections and ranking) and `HotkeyStore` (export/import of `StoredHotkey`); pass `report` in the constructor; let `AutomationService` register as the gate through a method that returns an `IDisposable`. **Medium** | Medium: 62 files use the registry, but the public API can stay the same |

### B2. Reaching into another class's data (chains)

| Where | Problem | Fix | Sev | Risk |
|-------|---------|-----|-----|------|
| `Pages/Gestures/GesturesPageViewModel.cs:89-101`, `GesturesPageViewModel.Restore.cs` (5 uses) | The page sets `settings.AreasScope`, `rule.Scope` and then calls `gestures.Apply()`: the page owns the service's write-and-apply step | Give `GestureService` intent methods (`SetAreasScope(scope)`, `SetScope(trigger, scope)`) that store, apply and save | medium | low |
| `Rules/RuleService.cs:76,77,120,131` | Reads `_gestures.Settings` and `_mouse.Settings` of concrete services (ctor :41) | Take two small read-only interfaces (`IGestureTriggers`, `IButtonMappings`) or `Func`s for what it needs | medium | low |
| `Pages/Devices/DevicesPageViewModel.cs` (`_service.Settings.Devices.IndexOf/Contains`, 5 uses), `DeviceItem.Check.cs` (2), `MethodItem.Scripts.cs` (2), `AutomationPageViewModel.Scripts.cs` (1) | Pages walk the service's settings list | `DeviceService.IndexOf(device)`, `Has(device)`, `Profiles` | low | low |
| `Pages/Volume/Easy/Flow/EasyEnvironment.cs` (3), `Audio/Engine/BusDefault.cs` (9), `Audio/AudioDefaults.cs` (6), `EffectsEngine.cs` (7) | `_mixer.Settings.Apps/Inputs/Combined` read everywhere | Read-only views on `MixerService` (`Programs`, `Inputs`, `CombinedDevices`) | low | low |
| `Shell/ShellViewModel.cs:153,155` | The same `() => _mixer.Settings.Apps.Select(a => a.ExePath)` typed twice | One `MixerService.RememberedPrograms()` | low | none |
| `Shell/ShellViewModel.Tray.cs:37` | The page that owns a state is found by `p.Title == state.Group`, string matching | Put the owning page id on `StateEntry` (or keep as is and note it) | low | low |

### B3. Pages and services reaching into each other

- Clean on the main rules: Core never references App; no page imports another page's namespace (the shared `Pages.Restore` is a neutral piece).
- `Pages/Volume/VolumePageViewModel.cs:135` sets `mixer.Report = report` (`Audio/MixerService.cs:39`): a page configures a service, and the
  mixer reports nothing until the Volume page is built. Fix: pass `report` to `MixerService`'s constructor in `ShellViewModel`. **Medium**, risk low.
- The `OmniControl.App.Pages` root namespace holds shared card models (`ToggleItem`, `CardGroup`, `ValueWheel`...) and is imported by
  `Controls/*`, `Fx/*` and `Picker/*`. This is not a real page dependency, but the name makes it look like one. Fix (later): move the shared
  pieces to `OmniControl.App.Cards` or similar. **Low**, risk: a large rename.
- Two classes called `PresetPicker`: `Controls/PresetPicker.axaml.cs:15` (the master control) and `Fx/PresetPicker.cs:37` (an effect's
  preset model). Same word for two different ideas. Fix: rename the Fx one `EffectPresetBox`. **Low**, risk none.

### B4. Static mutable state

| Where | Problem | Fix | Sev |
|-------|---------|-----|-----|
| `Services/AppSettings.cs:52` `AppSettings.Current` | A global settings object read and written from 10 files, including view-models (`Fx/EqPanelItem.cs`, `FxWindowViewModel.cs`, `Pages/CardGroup.cs`, `EasyViewModel.cs`, `ProgramListTools.cs`) and `Fx/Plugins/PluginFolders.cs:13` | Make one in `App` and pass it (or the one value needed) to each user; tests stop sharing it | medium |
| `Services/AccentService.cs:37-44` | Static `Name`, brush cache and `Changed` event; `AppSettingsPageViewModel.cs:73` subscribes and never unsubscribes | An instance service made at start-up; for now, unsubscribe in the page's `Dispose` | low |
| `Pages/ComboKeys.cs:24-25` (`_typed`, `_typedAt`) | Type-ahead buffer shared by every drop-down in every window | Keep the buffer per `TopLevel` (attached property or `ConditionalWeakTable`) | low |
| `Pages/RowEditing.cs:23` (`_activeSlider`) | One slider "being edited" for the whole app; two windows can confuse it | Same: per `TopLevel` | low |
| `Picker/PickerWindow.axaml.cs:22` (`_keys`) | Set once by the shell; already documented as set-once | Pass it through `ActionsWindowService` when the window is made | low |
| `Services/AppLifecycle.cs:9-13` | Static events; `Shell/WindowLifecycle.cs:83-85` subscribes for the app's lifetime | Acceptable (app lifetime); note it in `ARCHITECTURE.md` as the one allowed static event hub | low |

### B5. Duplicated logic

- Marshalling a report to the UI thread is written five times (`AutomationService.cs:65`, `GestureService.cs:37`, `MouseService.cs:367`,
  `RuleService.cs:49`, `HardwareDisplayItem.cs:199`). Fix: `ShellViewModel.Report` (`ShellViewModel.cs:284`) posts itself when off the UI thread
  (`Dispatcher.UIThread.CheckAccess()`), then delete the five wrappers. **Medium**, risk low. This also removes the cross-thread crash a new service
  would cause by forgetting the wrapper.
- Log files: each service has its own `LogFile` const and `Log` helper (`AutomationService.cs:26,366`, `DeviceService.cs:22,200`,
  `RuleService.cs:18,193`), plus a literal `"mouse-log.txt"` (`ShellViewModel.cs:163`), `osd-log.txt` and a page card writing its own log
  (`Pages/Displays/DisplayOrientationCard.cs:15`). Fix: one `Services/LogFiles` list (name and path per feature, like `DataStores`) and one
  `Log(feature, line)`; the display card asks its service to log. **Low**, risk none.
- `EasyFlowViewModel.cs:49-52` builds `RelayCommand`s by hand while the rest of the code uses `[RelayCommand]`. **Low**.

### B6. Event subscriptions without unsubscribing

Most `+=` without `-=` are a view or window subscribing to its own children, or app-lifetime objects (pages are built once), which is fine.
Worth fixing:

- `AppLists/AppScopeModel.cs:37-38`: `store.Changed += Refresh` and `group.PropertyChanged += OnGroupChanged`, and `store.Register(this)`, with no
  way back (`AppListStore.Unregister` is never called). Harmless while cards are built once per page, but any rebuild of gesture or mouse cards
  (`GesturesPageViewModel.cs:98`, `MousePageViewModel.cs:124`) would leak models and leave stale "Used by" rows in the manager. Fix: make
  `AppScopeModel : IDisposable` (unsubscribe and `Unregister`). **Medium**, risk low.
- `Pages/AppSettings/AppSettingsPageViewModel.cs:73`: subscribes to the static `AccentService.Changed`, never removed. **Low**.

### B7. Layer breaks

- Win32 in App: `Hotkeys/HotkeyService.cs:16-21` (RegisterHotKey, a message loop), `Osd/WindowsOsd.cs:118-123` (FindWindow, ShowWindow,
  keybd_event), `Services/ClickThrough.cs:12-13` (Get/SetWindowLongPtr). Fix: move each to `Core/Engine` (`HotkeyListener`, `WindowsOsdWindow`,
  `WindowStyles`) behind a plain API; the App classes keep only the decisions. **Medium**, risk low (no behaviour change).
- `Pages/Displays/DisplaysPageViewModel.cs:2,214`: a view-model imports `System.Management` to catch `ManagementException`; the engine should turn
  WMI failures into an `OperationResult`. **Low**.
- Decisions in views: `Picker/PickerMenu.axaml.cs` (B1), `AppListManagerWindow.axaml.cs:86,92` (A), `Tray/TrayFlyoutWindow.axaml.cs` (254 lines,
  mostly placement and focus, acceptable).

### B8. try/catch that hides failures

- Bare `catch` (every exception type, rule 15): `Audio/MixerService.cs:159`, `Audio/ProcessIcons.cs:27`, `Core/Engine/Audio/CoreAudio.Capture.cs:23,35`,
  `CoreAudio.Devices.cs:23,50`, `CoreAudio.Master.cs:25,34`, `CoreAudio.Meters.cs:28,62`, `CoreAudio.Sessions.cs:38,41`,
  `Core/Engine/LaptopBrightnessController.cs:52`, `Core/Engine/Screenshot/Mp4Writer.cs:67,171`, `RecordingSound.cs:134`. They also swallow
  `NullReferenceException` and `InvalidCastException`, so a real bug looks like "no device". Fix: `catch (Exception ex) when (ex is COMException or
  InvalidCastException ...)` as the rest of Core does. **Medium**, risk low (an unexpected type then surfaces, which is the point, so pair it with item 1).
- `Services/AppDataStore.cs:40-50`: a failed save is silent (no report, no log). See item 4.
- Empty typed catches with a reason (`Showcase/ShowcaseRun.cs`, `Adb/UninstallRegistry.cs`, `InstalledPrograms.cs`) are acceptable.
- Good: the eight unfiltered `catch (Exception)` are all deliberate isolation boundaries that log (`MouseHook.cs:166,195`, `InputQueue.cs:46`,
  `RuleRunner.cs:53`, `SingleWindow.cs:42`, `FxWindowService.cs:100`, `TrackpadWatcher.cs:139`, `Vst2Effect.Host.cs:66`).

---

## C. Fault isolation

What is guarded well: the mouse hook runs each handler in its own try/catch (`MouseHook.cs:187-195`), the mouse work queue keeps going after a
failed call (`InputQueue.cs:42-50`), rules (`RuleRunner.cs:53`), FX windows (`FxWindowService.cs:100`), single windows (`SingleWindow.cs:42`),
the trackpad watcher, `BrightnessWatcher.Poll` and `SimulationSetup.Recheck` catch their own failures, and `AudioPipe.StartAsync` turns COM errors
into `InvalidOperationException` that `EngineRun.StartPipe` handles.

What is not:

1. **No last-chance handler** (`App.axaml.cs`, `Program.cs`). Avalonia 11.3 has `Dispatcher.UIThread.UnhandledException`; nothing subscribes to it,
   nor to `AppDomain.CurrentDomain.UnhandledException` (only the elevated setup mode does, `EngineSetupCommand.cs:40`) or
   `TaskScheduler.UnobservedTaskException`. 27 files have `async void` handlers (`AutomationRuleRow`, `MouseButtonRow`, `RuleEditorWindow`,
   `MousePageViewModel.Pointer.cs:190,204`, `FxChainPanel.AddEffect`...). Fix: in `App.OnFrameworkInitializationCompleted`, handle
   `Dispatcher.UIThread.UnhandledException` (log to an `errors-log.txt`, show the calm one-sentence error with "Copy details" and the log path,
   `e.Handled = true`) and log the AppDomain and unobserved-task events. **High**, risk low; this one change contains most of the rest.
2. **Run path** `HotkeyRegistry.cs:187-200`: `try { run(action); } finally {...}` with no catch, and `Ran?.Invoke` unguarded. One broken action
   (a device script, the OSD, a screenshot) takes down hotkeys, tray, rules and gestures together, because they all run through here. Fix: catch,
   log with the action id and report "Couldn't run <title>." Run each `Ran` subscriber in its own guard. **High**, risk low.
3. **Start-up** `ShellViewModel.cs:119-195`: every service and page is made in one constructor; `_mouseHook.Start()` and
   `_devices.StartAutoChecks()` run inside it. A throw in any page constructor (Displays reads monitors, Volume reads the audio graph) means the
   window never opens. Fix: build each page through one `TryMake(() => new XPageViewModel(...), title)` helper that logs and leaves that page out
   (or shows a one-line "This page couldn't start" page), and start the background work (hook, auto-checks) after the window is shown. **High**,
   risk medium (start order is load-bearing; keep the documented order).
4. **Shut-down** `ShellViewModel.cs:309-339` and `WindowLifecycle.cs:90`: about 30 `Dispose` calls in one block. If, say, `_automation.Dispose()`
   throws, `_pointer.Dispose()` (puts the button swap back), `_effects.Dispose()` (puts every program's sound back, empties the APO chain) and
   `_mixer.Dispose()` never run, leaving the user's Windows changed. Fix: a small `Teardown` list that runs each step in its own try/catch and logs,
   with the steps that restore Windows state first. **High**, risk low.
5. **Timer threads**: `Services/ThrottledRun.cs:41-56` runs `_work()` on a `System.Threading.Timer` with only `finally`; an exception there ends
   the process (users: `DisplaysPageViewModel.cs:95` rescan, `EffectsSupportService.cs:33`, `ProgramDiscovery.cs:19`). `Core/Engine/UsbDeviceWatcher.cs:46-57`
   raises `Changed` on the timer thread unguarded (today's only subscriber posts, so it is safe by luck). Fix: catch-and-log in `ThrottledRun.Run`
   (one shared piece covers all three) and guard the invoke in `UsbDeviceWatcher.Scan`. **High** for ThrottledRun, low for USB.
6. **Data**: `AppDataStore.Save` (`:40-50`) writes in place with `File.WriteAllText`; a crash or power cut mid-write leaves a truncated file, `Load`
   (`:27-37`) then returns null on `JsonException`, the feature starts from defaults and its next save overwrites what was left. Fix: write to
   `name.tmp` and `File.Move(tmp, name, overwrite: true)`; on a load failure keep the bad file as `name.bad-<date>` and report it once.
   **High**, risk low.
7. **Cross-thread reports**: `ShellViewModel.Report` sets bound properties directly; a service that forgets the post wrapper (B5) throws on a
   worker thread. Fix as B5. **Medium**.

---

## Lessons for AGENTS.md

Recurring patterns in this code, phrased as rules:

- **Every entry point has a guard.** An action run, a timer or thread callback, an `async void` handler and each start-up and shut-down step catches,
  logs and reports its own failure; one feature's exception never reaches the next feature. Shared runners (`HotkeyRegistry.RunNow`,
  `ThrottledRun`, the teardown list) carry the guard once so callers do not.
- **Restore steps are separate.** Shut-down code that puts Windows back (audio, pointer, displays) runs each step on its own, first, whatever else fails.
- **Saves are atomic and failures are heard.** Write to a temp file and move it; never overwrite a file you could not read; a failed save or load is logged and reported once.
- **Report is thread-safe at the source.** The shell's report marshals itself; services never wrap it again.
- **Services keep their settings.** A page or another service calls an intent method (`SetScope`, `Rename`); it never edits `service.Settings` and then calls `Apply`/`Save`.
- **Constructor, not property, for what a class needs.** No settable `Report`, `Gate` or `Log` filled in later by someone else; a page never configures a service.
- **Take an interface or a `Func`, not a sibling service.** A service that needs two facts from another gets those two, not the whole class.
- **No bare `catch`.** Name the exception types, also in Core; a bug must not look like "no device".
- **No new statics that hold state**, including caches and "currently editing" fields in shared helpers; keep them per window or per instance.
- **One concern per partial file is not enough.** When a class passes about 1,000 lines over its partials or 8 constructor parameters, move a concern into its own service instead of adding another partial.
- **Delete with the change.** When a command, property or resource stops being used (a code-behind handler replaced it, a feature moved), delete it in the same task; a `[RelayCommand]` whose `XxxCommand` is bound nowhere is dead.
- **One name per idea.** Do not reuse a master control's class name (`PresetPicker`) for a different type elsewhere.
