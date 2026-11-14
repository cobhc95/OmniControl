# Handover

State of OmniControl for the next agent. Rules: `AGENTS.md`. File map: `ARCHITECTURE.md`. This file: what exists and how it is wired.

## What exists

| Page | What it does |
|---|---|
| Displays | Two collapsible groups. **Hardware**: one collapsed inner group per monitor (and the laptop panel) with what the monitor itself does over DDC/CI: brightness, contrast, red/green/blue gain, saturation, sharpness, speaker volume, colour preset, input source, turn screen off; controls the monitor does not report are greyed out with a tooltip. **Software**: digital brightness, contrast and colour temperature per display (gamma ramp), saturation for all displays, one "Reset everything / Reset all" card. Every Displays card is half width, two per row (see "Displays compact" below). The header line of Hardware shows "N monitors available" beside the title and the Refresh button (F5) at the right end: the master `pill` button, a header tool (`Tag="grouptool"`, an Up/Down stop like the Mouse layer picker). Master button/field classes and the new `pill glyph`, `TextBox.field`, `TextBox.bar`/`ComboBox.bar`, `ComboBox.tool` are listed in a comment in `Theme.axaml`; unverified on screen. |
| Volume | Two tabs (13 Nov, see "Volume page: Mixer and Wiring tabs"). **Mixer**: **Outputs** (master, programs, combined speakers) and **Microphones** (recording devices, combined microphones). Each row: icon, name, a sentence "→ <devices> · <effects>" (read only: the device words open Wiring at that row), pan (master, programs), level, mute, meter, an FX button, × for a remembered program or a combined device; the effects link ("2 effects", "1 effect, off") and FX open the row's FX window. **Wiring**: Sound mode (one global mode, panel with status and setup), the virtual cable chip and **+ Combine devices** on top; tables Source | Plays on | Cable and Source | Records from | Cable (chips with x and "+ add", or one choice where the mode takes one device); combined device cards (members, cable, Edit, ×). Choices the mode can't do are dimmed with the reason and a Switch link. |
| OSD | Custom pop-up for volume/brightness; options; hide Windows' own OSD. Show OSD + Hide Windows' OSD are half-width cards. |
| Mouse and Trackpad | Group "Pointer settings" (last): Windows' own mouse settings changed live, scheme and single pointers, reset and restore (see "Mouse and Trackpad page: Pointer settings"). Group "Mouse Buttons": remap buttons, wheel and tilt like X-Mouse Button Control; group "Trackpad": three/four-finger swipes and pinch in/out on a precision touchpad, same cards, actions and layers (see "Mouse and Trackpad page: trackpad gestures"). 10 layers (hold a button for layer N, or a hotkey cycles); no profiles (removed at the owner's request, so no per-program mappings), actions = clicks, scroll, media/volume, window, desktop, edit, browser, custom keys, run a program, or any OmniControl hotkey action. Settings in `mouse.json`. Hotkeys: toggle remapping, next layer. |
| Gestures | Mouse-wheel gestures: scroll over the taskbar, top/left/right screen edge, a window title bar or the desktop, or scroll while holding Ctrl+Alt, Ctrl+Shift, Alt or Win. Each is a switch plus an action (volume, brightness, contrast, mic level, window opacity, virtual desktop, next/previous track, zoom, sideways scroll, pointer speed). Middle-click gestures: a middle click over the taskbar, a window title bar or the desktop runs any action a mouse button can have. All start off. A last group "Custom rules" holds rules built as a sentence (see "Custom gesture rules"). Edge strip width is a setting; its "Show margins" switch draws the areas on screen while the page is open. Hotkey: toggle gestures. Settings in `gestures.json`; rules in `rules.json`. |
| Shortcuts | One card per registered hotkey action; rebind, pick devices. |
| App settings | Theme and accent gallery cards, start with Windows, start minimised (half-width pair), "When I close the window" choice (Ask each time / Minimise to tray / Exit completely), plug-in folders (add a folder, one row per folder with Remove, Scan now), About. |

Also: FX chain window (per program/master/input, Reaper-style), Add effect window (built-ins + VST3 and VST2 scan and file load), rubber-band selection, drag-reorder, tray icon, orange multi-size icon (`Assets/OmniControl.ico`), wrap-around Up/Down navigation.

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

Keyboard: `RowEditing` (inside a card list), `GroupedCards` (pages with collapsible groups), `MainWindow.axaml.cs` (sidebar). All wrap (`Services/Wrap.cs`). A slider's value is typed by a double-click on the value or Ctrl+Enter on the slider (`Pages/NumberText.cs`; Enter, Tab or a click elsewhere applies, Esc cancels). `HalfPanel` lays out half cards in list order.

Persistence (all in `%APPDATA%\OmniControl`): `settings.json` (AppSettings), `mixer.json` (MixerSettings incl. FX chains, VST3 plug-in states (`FxSlot.State`), the capture test result `CaptureSilence`; input chains keyed `input:<endpointId>`), `hotkeys.json`, `plugins.json`, OSD settings, `mouse.json`, `gestures.json`, `rules.json`, `software-display.json` (original gamma ramps), `pointer-settings-original.json` (the user's Windows pointer settings and pointers before OmniControl's first change; written once, kept), `pointer-swap-trial.json` (only while a button swap waits for Keep), `osd-log.txt`, `mouse-log.txt`, `rules-log.txt` (diagnostics), `engine-journal.json` (only while the engine has silenced or routed a program), `effects-setup-log.txt` (every setup step: OmniControl APO install/remove, Equalizer APO and VB-CABLE installers, failures with their detail). Master effects go to `OmniControl.txt` in the user's Equalizer APO config folder (usually `C:\Program Files\EqualizerAPO\config`) plus one marked `Include: OmniControl.txt` line in its `config.txt`; with the optional OmniControl APO the folder is `C:\ProgramData\OmniControl\APO`.

Adding things: **page** = VM + view + one line in `ShellViewModel` + one DataTemplate; **action** = `HotkeyRegistry.Register` once (a level: `HotkeyAction.ForLevel`; an OSD pop-up: `OsdService.Declare`), see "Master lists" in `ARCHITECTURE.md`; **timing value** = `Timing.cs`; **half card** = `Half = true`.

## Known limits / open items

- FX chains run through the effects engine in the global sound mode (see the last section): Fast = Equalizer APO for the master and for each microphone it is on (the three equalisers, Gain, IR Loader, VST2 plug-ins; VST3 saved only there), Capture and replay for programs; Full control = OmniControl's engine through virtual cables for the master, programs, microphones and combined devices (the three equalisers, Gain, VST3 and VST2 plug-ins). Other built-in effects are stored only and the UI says so; combined microphones run only in Full control. Porting TabForge's other DSP (compressor, limiter, delay, gate, reverb, convolution) is the next step for the program engine.
- Meter ballistics (floor -60 dB, warm -12 dB, hold 1.2 s, fall 28 dB/s) are my own; TabForge's `MasterMeterModel` was not compared.
- Half-width cards pair only when the list is at least ~500 px wide (`HalfPanel.MinPairWidth`); at the default window size they may stack. Tune if wanted.
- Pan needs Windows to allow per-channel levels on a program's session (`AudioSession.CanPan`).
- Per-app output device uses an undocumented interface (`CoreAudio.Routing.cs`).
- Hiding Windows' OSD is best-effort.
- Recent UI work (inputs group, meters, input FX, theme gallery, half cards, accent tint, icon) was built and launched but not confirmed on screen by the owner.
- Code audit against rules 11-16 done: json storage unified, silent catches explained, duplicate percent maths moved to `VcpValue.Percent`. Remaining by design: `AppSettings.Current` and `AccentService` are shared singletons (read by `CardGroup`, FX window); inject them if more pages need them.
- OSD "Show in OSD" is half-width cards named after the thing shown; it now also covers USB devices, play/pause and track/artist. Those three use one text style whatever the OSD style is set to. The build output folder is now `OmniControl.App\bin\Release\net9.0-windows10.0.19041.0\` (net9 since 13 Nov) (the target framework gained the Windows 10 version for the media API). USB and media pop-ups were built but not tried by the owner; the USB name is Windows' own device name, which can be generic for some devices.
- Keyboard: Up/Down on a page's cards loops inside the page; only Tab goes to the sidebar (the sidebar's own two lists still hand over to each other).
- Gestures page was built and launched but not tried by the owner. Known limits: one wheel notch is one step (no speed scaling); no per-app volume gesture; wheel up/down cannot be swapped per gesture; the title-bar test asks the window under the pointer (50 ms limit) and some apps with custom title bars may not report a title bar; the taskbar test uses Windows' taskbar class names; area gestures only fire with no modifier key held. A gesture that is on swallows the scroll in that area.
- Middle-click gestures (group "Middle click over a screen area", keys `middle.taskbar`, `middle.titlebar`, `middle.desktop` in `gestures.json`; a missing rule is off). Built and launched, not tried by the owner or on screen. Choices are the Mouse page's action list without "Default behaviour" (that would just mean off). Defaults when first switched on: Taskbar = Task view, Window title bar = Minimise window (the window under the pointer, i.e. the one clicked), Desktop = Show / hide desktop. How it runs: `GestureService.MiddleClickAt` is asked by `MouseService` on every press before the middle button's mapping (gestures win; with no gesture there the press goes to the mapping as before). The gesture's action then goes through the same pipeline as a mapped button: swallowing the press and its release, Trigger (press / release / repeatedly / keys held), Times, modes and layer actions, "Also send the button's normal click", the launch limiter of the one `MouseActionRunner`. It works while Mouse page remapping is off (it only needs gestures on). As for wheel area gestures, it does not fire while a modifier key is held, and edges are not areas for it (a middle click in an edge strip over the desktop counts as the desktop). Over OmniControl's own windows only left/right are exempt (`OwnProgram`), so a middle click on OmniControl's own title bar can trigger the title-bar gesture; the page hint's sentence "OmniControl's own window is never affected" was already inaccurate for wheel gestures (the code has no such check) and was left for the owner to decide. The gesture card's pill now takes the space after the name (as on the mouse button card), so long action names wrap in the pill. In search, the wheel and middle-click "Taskbar"/"Desktop"/"Window title bar" cards both show as "Gestures"; a card also matches its group's name ("middle click").
- Gestures "Show margins" (owner-approved mockup; built and launched, nothing seen on screen by the agent). A switch on the "Edge strip width" card's line (`SliderItem.Companion`, off at every start, not saved). While it is on and the Gestures page is on screen (open page, main window showing and not minimised), `App/Gestures/MarginOverlay` draws every gesture area whether its gesture is on or not, from the hit test's own geometry (`Core/.../AreaMap`): each monitor's top/left/right edge strips (`PointerArea.Edges`, the rectangles the hook tests, with the same `GestureSettings.ClampedEdgeStrip()`; all monitors, as the hook treats every monitor's edges), every visible taskbar window, and the active window's title bar mapped with the hook's own question (`PointerArea.IsCaption` = WM_NCHITTEST says caption, via `CaptionArea`). Overlaps are cut in the hook's order (taskbar, then edges, then title bar), so only the winning part of each area is drawn. There is one window per monitor (`MarginWindow`), covering the monitor and cut by a window region (`Core/Engine/OverlayPlacement`, `SetWindowRgn`) to the area rectangles and labels, which are drawn inside it at exact physical pixels, so a 2 px strip is 2 px and the right strip is exactly monitor right minus the width. (Earlier versions gave every strip its own small window; Windows clamps a top-level window up to its minimum tracking size, which is why the drawn strips never got thinner than about 20-55 px. Unverified on screen that the region-cut window now shows 2 px.) Labels (app base text size) sit just inside the screen beside their area on the side with the most room (right of the left strip, left of the right strip, below the top strip, above a bottom taskbar, below a title bar near the top) and are pushed clear of every area and earlier label; the pill "Edge strip width: N px" sits above the bottom centre of the primary screen's working area, pushed up past anything it would cover. Edge strips are solid accent with a dashed inner edge; the taskbar and title bar are hatched and outlined so a title bar under a thin top strip is not mistaken for it. Moving the slider redraws at once; otherwise every `Timing.GestureMarginsRefresh` (250 ms), looked up off the UI thread. Leaving the page, hiding/minimising/closing the window or turning the switch off closes all its windows and stops the timer. Hotkey "Toggle gesture margins" (`gestures.margins`). Where the drawing and the hook can still differ (not drawn): (a) the hook asks the window under the pointer, the overlay maps the active window's title bar only, so another window covering it, or title bars of inactive windows, are not shown; (b) the taskbar is drawn as its window rectangle, the hook asks the class of the window under the pointer, so anything covering the taskbar (Start menu, flyouts) differs; (c) the title bar is sampled every 16 px per row: a gap narrower than that between two samples is drawn as title bar; it is mapped again only when the active window changes, moves or resizes (a title bar whose layout changes in place, such as browser tabs, stays as first mapped), and a window that stops answering gets no title bar drawn; (d) not drawn: the desktop area, the exemptions (area gestures do not fire while a modifier key is held; the Gestures switch off; gestures do not reach programs running as administrator), and middle-click gestures, which ignore edge strips (the hook passes strip 0), so for them the taskbar and title bar reach to the screen edge. Unverified: (1) click-through: the pieces use `ClickThrough` (WS_EX_TRANSPARENT without WS_EX_LAYERED, like the OSD); if Windows still hit-tests them, a click on a strip, label or title bar could land on the overlay, and `WindowFromPoint` in the hook could see the overlay instead of the taskbar or title bar below it while the margins show. If so, add WS_EX_LAYERED for these windows. (2) Mixed-DPI multi-monitor sizing of labels. (3) Cost of mapping a very wide title bar (thousands of WM_NCHITTEST questions, off the UI thread, once per window change). Keyboard on that card: Left/Right move between the slider and the switch (the card now has two parts), Enter on the slider then lets Left/Right change it. At the narrowest window and in search results the "Show margins" name wraps and then trims; the slider gets narrow there (the switch never shrinks).
- New shared hook: `PageViewModel.OnOnScreenChanged(bool)`; `WindowLifecycle` reports window visibility/minimise through `ShellViewModel.SetWindowShown`. The FX split button's divider style is now the shared `Border.divider`.
- Mouse page was built and launched but not tried by the owner. The action picker lists every entry of X-Mouse Button Control's action list from the owner's screenshots, in 14 categories (`MouseActionCatalog`; each action also has search keywords, so the shortcut or the X-Mouse name finds it). Actions that last while a button is held or until it is pressed again (sticky click-drag, axis locks, slow pointer, movement-as-scroll, drag/size window, reveal desktop) live in `MouseModes`; 10 layers. Every action with something to tune has a cogwheel (in the action window's list and on the card) opening a settings page: when it runs (pressed / released / repeatedly while held / keys held), times and repeat speed, window target (under pointer or active), scroll distance/amount/direction/reverse/sticky, slow and cycled pointer speeds, sticky auto-release, web search engine, run-program arguments/folder/window/admin, layer wrap and message; wheel gestures have reverse, steps per notch, opacity and pointer-speed steps. Settings only show when they matter (`ActionOption.ShownWhen`: repeat speed only while repeating, custom address only for a custom engine, reverse vertical/sideways by scroll direction). Lasting actions (movement as scroll, slow pointer while held, axis locks while held, drag/size window, reveal desktop, click-drag) have a "Pointer while held" setting: Windows pointers shown everywhere (the default for movement as scroll is four arrows) (four arrows, up/down, left/right, hand, crosshair), put back on release by reloading the cursor scheme (`Core/.../PointerShape.cs`, `App/Mouse/PointerLook.cs`); `PointerLook` also resets the scheme at start-up. Almost every action also has two opt-in extras (not layer or do-nothing actions): "Also send the button's normal click" (the original input is not swallowed) and "Also send this shortcut". Options are defined in `MouseOptions.cs` and `GestureOptions.cs` and stored after the action ("id|key=value"). The option list was designed from X-Mouse Button Control's user guide as summarised by a research agent (the official PDF could not be read, so exact labels and ranges there were not verified). Not done compared with X-Mouse Button Control: Button Chording and Button Held (the two entries shown as "(undefined)" in the screenshots: actions that depend on holding or combining buttons), timed (long-press) layer transitions, layer names and excluding layers from cycling, "block original input" and "only when this program is active" per action, a global delay between simulated keystrokes, profiles of any kind (per-program or per-window mappings). Old `mouse.json` files with profiles keep only the default profile's mappings. Windows blocks the hook for programs running as administrator unless OmniControl does too. Over OmniControl's own windows the left and right buttons are never remapped (so a bad mapping can always be clicked away); the middle and side buttons, wheel, tilt and gestures work there like anywhere else (they used to be skipped there entirely, which confused the owner while testing).
- Mouse hook diagnostics (deliberate, keep them): `%APPDATA%\OmniControl\mouse-log.txt`: hook installed or refused, the first inputs it sees, swallowed inputs, handler errors, handlers slower than 50 ms (CoreTiming.HookSlowMs), and (own budget) the movement-as-scroll path: started at x,y and whether the four-arrow pointer was shown, each batch of notches sent and how many inputs Windows accepted (with the error code when 0), "pointer had moved; put back", stopped and whether the pointer was restored. Each kind is capped at 150 lines per run (CoreTiming.HookLogLimit), and lines are written by a background thread so the hook never waits on the file. Builds go to `OmniControl.App\bin\Relaunch<HHMMSS>\` because dead OmniControl processes keep the normal output files locked.
- Movement as scroll (redesigned): the hook's move handler only swallows the movement and adds up how far the mouse moved (a swallowed movement leaves the pointer still, so reported position minus the held spot is the exact distance); the `InputQueue` thread turns it into whole notches (distance per step from the action's options), sends them with one SendInput per batch, and puts the pointer back if anything moved it. Before, SendInput, SetCursorPos and log file writes ran inside the hook callback, which holds up all mouse input on the system: the likely cause of the lag. Wheel input from SendInput is routed by Windows exactly like a real wheel: to the window under the pointer when "Scroll inactive windows when I hover over them" is on (the Windows default), otherwise to the focused window. A step on one axis drops the drift collected on the other. The four-arrow pointer (option "Show the four-arrow pointer while scrolling", on by default) replaces the arrow, text, hand and busy pointers via SetSystemCursor and puts saved copies back on release; if a copy cannot be restored, Windows' cursor scheme is reloaded. Open: if OmniControl is killed while the button is held, the four-arrow pointer stays until the cursor scheme is reapplied (Settings > Mouse pointer) or the user signs out. Not tested on screen by the agent.
- Other modes (`MouseModes`) were moved off the hook thread the same way: double-click-drag, sticky buttons, held keys, reveal desktop, slow pointer and window drag/size now run on `InputQueue`; window drag follows only the newest pointer position. Axis locks still call SetCursorPos inside the hook (it is instant and the next movement must be measured from the locked line).
- OSD "slow to appear" (owner: slow from launch and after idle). Measures, all built, none measured on screen: (1) `OsdWindow` is created and shown parked off screen as soon as the dispatcher runs (Send priority, before the main window's pages) and draws Contrast, Brightness and Volume once each, one real frame apiece, so fonts, shapes, render thread and graphics device are ready; (2) between pop-ups it keeps its last frame drawn off screen instead of going to opacity 0, so a pop-up with the same look is only a window move (the already drawn frame appears at once; the new value follows one frame later); a changed look (other element, style, opacity, text pop-up) is drawn first and the window moves after that frame, so an old pop-up never flashes; (3) a 1 s keep-warm timer redraws the parked pop-up off screen so the drawing pipeline and its memory never go cold; (4) `ProcessPower`: above-normal process and UI-thread priority, `SustainedLowLatency` GC, and `KeepResident` (hard minimum working set = memory in use after start-up) besides the existing no-throttling and 1 ms timers; (5) hotkey and mouse-hook threads run at highest priority, hotkey actions are posted at Send priority; (6) brightness/contrast: a level older than `Timing.DisplayLevelTrust` is no longer re-read before the pop-up (a DDC/CI or WMI read, 40-200 ms, was in front of every first step after 10 s idle); the pop-up shows at once and the writer thread re-reads, then shows a corrected value if the display was changed on its own buttons. Unverified: whether the parked window really keeps its frame on every composition mode, whether `KeepResident` is granted (it is refused silently if Windows cannot spare the memory), and the real latency. Not done: ReadyToRun (only applies to `dotnet publish`); laptop-key brightness changes seen by `BrightnessWatcher` do not update `LevelStepper`, so the first step after such a change may show a corrected value a moment later. The window stays parked off screen (never on screen at opacity 0) so it can never block the pointer. Open: ended OmniControl processes do not go away. On 12 Nov five killed instances were still listed, each with one thread left, 150-310 MB of memory and 2,000-7,000 handles. A thread stuck in a driver call (the graphics or display driver, or DDC/CI) is the likely reason. Together they hold over 1 GB, which adds memory pressure and paging. Only a restart clears them. Find which call never returns.
- OSD latency: hotkey actions (also from gestures and mouse buttons) run at Send dispatcher priority, above input, layout and rendering; the pop-up's level visual is updated in place instead of rebuilt per step; brightness/contrast steps no longer read the display before every step (a DDC/CI read is 40 ms+, and steps queued behind each other, so a wheel spin lagged by seconds): `LevelStepper` trusts the level it last set or read for `Timing.DisplayLevelTrust` and writes only the newest wanted level. Outside volume changes no longer re-show (possibly older) values right after OmniControl's own volume steps (`OsdService.NoteOwnChange(element)`, per element now); mute toggles show their pop-up directly.
- OSD "occasional stutter in its movement" (owner: mainly after a cold launch or an idle spell). Built and launched, nothing seen on screen or measured. Causes found and fixed: (1) **the volume poll**: every 16 ms a thread-pool timer asked Windows for the master volume twice, each time creating a new device enumerator, device and volume object that were never released (about 360 COM objects a second for the garbage collector and its finalizer, plus calls into the audio service); replaced by `CoreAudio.MasterWatcher` (Windows calls in on a change; follows the default device; nothing polled), and the unused `SystemVolumeController` / `ISystemVolume` / `MonitorEngine.System` were removed; (2) **the brightness poll**: a WMI query every 100 ms even on a desktop, where it fails (likely by throwing) ten times a second; `Core/Engine/BrightnessWatcher` (moved from App, rule 8) now stops polling when the first read finds no laptop panel or once the WMI event has fired; (3) **mic steps and the master mute toggle ran on the UI thread** (listing recording devices and Core Audio writes in front of the pop-up's frame); they now run on a worker like the master steps, the mic pop-up shows before the writes; (4) **queued values**: values posted from other threads are coalesced in `OsdService.Show` (only the newest waits; one post at a time, no closure per value); (5) **per-value work in the pop-up**: the Compact pill's bar changed length when the number went from 2 to 3 digits (the number column was Auto); every number now sits in a box as wide as "100" (look change: the pill's bar is constant length, a few pixels shorter at 1-2 digit values); bars scale their fill instead of re-laying out a grid; the ring uses Avalonia's `Arc` (same geometry as before) instead of building a new path per value; number texts are made once; `OsdSettings.ResolveAccent` no longer runs LINQ per value; the window is not moved (SetWindowPos and click-through re-applied) on every value while it is already in place; (6) **first-run code**: start-up warm-up now also draws a text pop-up and each level pop-up at 0 (muted look), 100 and 50, one frame each; `TieredPGO` is off (no instrumented re-compile of hot code in the first minutes); (7) `KeepResident` ran at the first idle moment, which could come before the warm-up's frames were done; now it runs `Timing.KeepResidentAfterStart` (5 s) after start-up. Weighed and not done: TieredCompilation or QuickJit off (start-up of the whole Avalonia app would JIT fully optimised, and a code path not warmed would be slower the first time, not faster), `PrepareMethod` (the warm-up runs the real path, which also loads types and fonts), ReadyToRun (publish only), GC settings beyond `SustainedLowLatency` (the garbage sources were removed instead). Still possible and not controllable from code: GPU/CPU power states after idle (the 1 s keep-warm redraw is the mitigation), DWM composition of a window that was off screen. Unverified: that Windows' volume notification arrives for every change source (it is documented to), the `Arc` look at 100 % (a full circle, as before), and the brightness poll stopping on a laptop whose WMI event never fires (the poll then keeps running as before). Noticed, not changed: the microphone pop-ups ("Microphone", "Microphone on/muted") draw the brightness sun icon (`OsdView` picks the speaker only for "Volume" labels), and the `ShowMaster` row sync is posted once per step. Gestures over a window title bar still ask that window a question inside the hook (up to 50 ms if it is busy).
- Explorer flood after "layer 2 + Back" (12 Nov). Evidence: `mouse-log.txt` shows no OmniControl launch and no key sent on layer 2: Back was unmapped and passed through untouched. But every pass-through Back started a new `explorer.exe /factory,{75dff2b7-...} -Embedding` process (a folder window) at the same second (16:21:51, 16:24:57, 16:25:14), and Winlogon logged "the shell stopped unexpectedly" at 16:22:06 with ShellBrowserCancel error reports. So the native side-button click, delivered to the window under the pointer, is what Windows turned into Explorer windows (most likely through WM_APPCOMMAND "browser back" reaching the shell from a window that has no back, e.g. OmniControl's own window or the desktop). Which window it was is not proven. Changes: (1) unmapped Back/Forward over OmniControl's windows or the desktop are swallowed (`MouseService.PassThrough`); (2) every pass-through is logged with the class of the window it goes to, so a repeat elsewhere names the window; (3) Launch-category keystrokes (Win+E, Win+R, mail, browser) now count against the launch limiter like programs and folders; (4) a new press of a button first ends whatever its previous press left running (held keys, repeats, hold-for-layer) when its release was lost, so keys cannot stay held. Held keys were already let go on quit (`MouseService.Dispose` -> undo actions -> `InputQueue` drains); a crash still cannot release them. Open: if the log shows pass-throughs to another window class before an Explorer flood, extend the "nowhere to go" check to it.
- No automated tests exist; the owner tests by hand (rule 7).

## Open items from the design-principles audit (not yet done)

Found by a read-only audit against the "Global design principles" in `AGENTS.md`. Fixed already: wheel over sliders, drop-downs and the gallery (`Pages/ValueWheel.cs`, `GalleryRow`), toggle-switch hover no longer changes border thickness, the EQ type drop-down is centred; double-click reset on the EQ Frequency/Q and effect Wet sliders (defaults from the model); typing a slider's value (`Pages/NumberText.cs`); the "on" green as `BrushOn`/`BrushOnHover`; the theme gallery reads the sidebar colours from each theme's dictionary; `TextBlock.hint`; the shared parts of `MixerRow`/`InputRow`, the action-card picker (`MouseActionWindow.ChooseForAsync`), dot building, `Services/Wrap.cs`, one slider step method; timings and counts named; HotkeyRow device list wraps; `ToggleRow` switch column is `Auto`. Still to do:

- **Font sizes and spacing:** about 40 explicit `FontSize` values and about 190 literal paddings, margins and heights bypass the shared styles (worst: `Fx/FxChainPanel.axaml`, `Shell/MainWindow.axaml`, `Pages/Volume/VolumeRowView.axaml`, `Picker/PickerMenu.axaml`). Left: a sweep that touches every view needs the owner's eye on each page.
- **Duplicated pieces:** the group-panel block is repeated on every page; the editor label column and its margin are repeated in each row view. Left: no low-risk shared form found in this pass.
- **Views that decide:** key routing in `Picker/PickerMenu.axaml.cs` and `Hotkeys/HotkeyRow.axaml.cs` belongs in a view-model. Left: moving it changes focus handling that the owner has not re-tested.
- **Accent tint in `Fx/FxChainPanel.axaml`** (the rubber-band selection: accent at 15 % opacity) kept: `BrushAccentSoft` is opaque (mixed with the page background), and the band is drawn over the rows, so it must stay see-through.
- **Search results column** (`Shell/MainWindow.axaml`, `ColumnDefinitions="100,*"`) kept: an `Auto` title column would let a long title squeeze the editor beside it at the minimum window width. The title and category trim with an ellipsis instead.
- **Typing a number:** the master picker's settings page (`PickerMenu` / `OptionItem`) keeps its own box and focus handling; only the reading rule (`TypedNumber`) is shared with `NumberText`. The pan value (shows L/C/R) is not typeable. Not tried on screen.

## Open items from the lean-code audit (12 Nov) - not done

Done in that pass: dead members removed (`CanStepBy`, `EqBand.Clone`, `FxCatalog.FindOrNull`, `EqPanelItem.ResetSelected`, unused display constants), `Services/Palette.cs` (danger red, dark background, charcoal), `Core/Models/Percent.cs` (all percent and pan maths), `Core/CoreTiming.cs`, `MixerService.MasterKey`, version read from the assembly (`<Version>` in the csproj), `Wrap.Direction`, `Grace.Within`, `TextMatch.Any`, `RemoveMissing` in `VolumePageViewModel`. Still open:

- **Namespace** `OmniControl.App.Pages.AppSettingsPage` cannot be renamed to match its folder: a namespace `Pages.AppSettings` collides with the settings class `AppSettings`. Rename the class (for example `AppPreferences`) first.
- **Rule 8, Win32 in App:** `HotkeyService` (RegisterHotKey/GetMessage), `WindowsOsd` (ShowWindow/keybd_event) and `ClickThrough` should each move to a small `Core/Engine` class.
- **Pages -> Pages imports:** done 13 Nov: the picker moved to the neutral `App/Picker` (see "Custom gesture rules").
- **Views that decide:** key routing in `PickerMenu.axaml.cs`, `HotkeyRow.axaml.cs`, `FxChainPanel.axaml.cs`, `RuleEditorWindow.axaml.cs` (Left/Right in the sentence) (only the Up/Down direction is shared so far).
- **Duplicates left:** typing state machine (`NumberText` / `OptionItem`), pointer-drag session (`ListDragReorder` / `RubberBandSelect`, 4 px vs 3 px), `GestureService` / `MouseService` snapshot code, the editor label column in five row views (a `ColumnDefinition` cannot take a style), literal paddings and font sizes.
- **Hard-coded left:** the meter colours now live in `Palette.MeterGreen` / `MeterAmber` (`#3FB36B` still differs slightly from `BrushOn` `#3FB950`; unifying would change the look), OSD pixel sizes, `CoreAudio.Routing.cs` string length, `Fx` mix units (the equalisers' frequency and gain texts are now one helper, `Fx/DrawKit`) in `Fx` windows.
- **Static state and catches:** `AppLifecycle` events, `AccentService.Changed` never unsubscribed (except by `OsdService`), `RowEditing._activeSlider`, two static `Guid _ctx`; 23 catch-all blocks not yet narrowed; `HotkeyService` `ManualResetEventSlim` and the `GestureService` hook handler are not released in `ShellViewModel.Dispose`.
- **`VolumePageViewModel`** partials (`.Hotkeys`, `.Inputs`, `.Rows`: about 18 `CoreAudio` calls) should go through an `App/Audio` device-control service.
- **Words:** docs say "switch", code says "Toggle" (`ToggleItem`, `ToggleRow`); pick one when next touching the docs.
- **ARCHITECTURE.md** now lists every file (checked 13 Nov by matching every `.cs`/`.axaml` name against it).

## One master list (12 Nov) - built, checked with a clean build only, nothing seen on screen

Every place that listed by hand what the app can do now reads one master list (table in `ARCHITECTURE.md`, "Master lists").
- **Actions:** `HotkeyAction` is the master entry. New metadata: `Group` (optional; for Displays "Hardware" / "Software") and `Heading` (category and group joined by `Services/Heading.Join`); `HotkeyAction.ForLevel` declares a level (`HotkeyLevel`: stored id + name) and its direction once, and builds both the plain run (`DefaultStep`) and the amount-aware step (this replaced the hand-written `_ => Step(+DefaultStep), stepBy: ...` pairs in Displays and Volume). Every page passes its own `Title` as the category (the typed "Displays", "Volume"... strings are gone). The dead `ShowGroupHeader` was removed.
- **Mouse button and middle-click pickers** take `HotkeyAction`s and list each under its `Heading` with its keywords (before: all under one category "OmniControl", no keywords). They now follow `HotkeyRegistry.Actions` changes: before, the Mouse page and the Gestures page took a copy at their construction, so actions registered by later pages (Toggle gestures, Toggle gesture margins, Show OmniControl window) were missing from both pickers, and a button or middle-click gesture already set to one of them showed "Default" / the first action on its card.
- **Wheel gesture picker:** the four level steps (volume, brightness, contrast, microphone) are no longer typed in `GestureCatalog`; every declared `HotkeyLevel` is offered, under the heading of the page that declares it. Stored ids are unchanged ("volume", "brightness", "contrast", "mic"), so `gestures.json` keeps working. `HotkeyRegistry.RunLevel(levelId, up, percent)` replaced `RunBy(actionId, percent)`.
- **OSD elements:** `OsdService.Elements` / `WarmLabels` arrays were replaced by `OsdService.Declare(name, isLevel)`: Displays declares Brightness and Contrast, Volume declares Volume and **Microphone** (new switch: the microphone pop-ups used their label as the element, so they could never be switched off), the service itself declares USB devices, Play / pause, Track and artist. The OSD page's "Show in OSD" group is rebuilt when an element is declared later. The start-up warm-up draws every declared level.
- **Search:** pages build entries with `PageViewModel.Entry`, whose category is always the page's title; a result shows `SearchEntry.Heading` (category, then group). The sidebar filter also keeps a page when one of its registered actions matches the words, so a new action's keywords never need copying into the page's `Keywords`.
- Visible changes (unverified on screen): (1) mouse/middle-click picker: the "OmniControl" category is replaced by one category per feature (Displays, Volume, OSD, Mouse, Gestures, App settings) at the end of the list, and those actions are also found by their keywords; (2) wheel gesture picker: "Sound and display" is replaced by "Displays" (Brightness, Contrast) and "Volume" (Microphone level, then Volume, the order the Volume page registers them); (3) Shortcuts search results read "Shortcuts - Volume" etc. under the title; (4) a "Microphone" switch in Show in OSD (after Volume); (5) pickers now also offer Toggle gestures, Toggle gesture margins, Show OmniControl window; (6) the sidebar keeps more pages while searching (e.g. "louder" keeps Volume); (7) the warmed-up pop-up that is ready first is now Brightness instead of Volume (declared order), so the very first volume pop-up after start-up may take one extra frame.
- Remains hand-written, with reasons: `MouseActionCatalog.Fixed` (built-in actions that are not OmniControl actions; correct place) but its per-id side lists (`WindowActions`, `RepeatableDesktop`, `OptionsOf`, `IsMode`, `HasPointerLook`) repeat ids of the same table: fold them into `Def` fields when that file is next touched; the built-in "Volume up / Volume down / Mute" (media keys) duplicate OmniControl's own Volume actions by name in the picker, and "Turn monitors off" will duplicate the planned Displays "turn off screen" action: remove the built-in once the OmniControl action exists, mapping stored "power.monitoroff" / "volume.*" to "omni:<id>" in `MouseSettings` load so saved buttons keep working (not done: it changes saved mappings); `OsdView` picks the pop-up icon (and the red muted look) by the label's first word, so a new level element gets the sun icon: the icon should become part of `OsdElement`; the gesture triggers' default actions name level ids ("volume", "brightness") as text; the Mouse page's `Buttons` (physical buttons, not actions); page `Keywords` (sidebar words for the page itself). Device scope for mouse and gesture use of an action is the scope picked on the Shortcuts page (they run the same `HotkeyAction`).
- The Displays Hardware/Software work from that note was done on 12 Nov (next section).

## Volume page: row height and duplicate programs (12 Nov)
- Mixer rows made shorter (unverified on screen): `LevelMeter` height 36 -> 28 (it set the row height), `ListBox.rows.compact ListBoxItem` padding 10,3 -> 10,2 and margin 3 -> 2 (shared, so the other compact lists such as Shortcuts, App settings and the FX editor are a little tighter too), and the extra 1-2 px margins removed from `MixerRow`, `InputRow` and `AddProgramRow`. The row is now driven by the two text lines; buttons stay 26 px.
- Duplicate "Claude" rows: programs are keyed by exe path and Microsoft Store apps live in a versioned folder (`...\WindowsApps\AnthropicPBC.Claude_4.23118.1.0_...` vs `_5.1231.2.0_`), so each update made a new entry (the old path no longer exists, hence the letter icon). `AppAudioEntry.KeyFor` now drops the version segment; `MixerSettings.Load` merges existing duplicates (most customised entry kept, newest path), and `MixerService.Scan` follows a program to its new path. The merge is written on the next save. Unverified on the owner's data.

## Displays rebuilt: Hardware and Software groups (12 Nov) - compiled only; nothing seen on screen, no real monitor or display setting was changed

**What exists.** `Pages/Displays/` (file map in `ARCHITECTURE.md`). One descriptor per slider control (`DisplayControls.cs`) makes its card, its up/down actions, its level id for the wheel gestures and its OSD declaration. Hotkey actions (merged 12 Nov, see "Displays: one action per control" below): ONE action per control, not per monitor. New OSD elements: Red/Green/Blue gain, Saturation, Sharpness, Speaker volume (hardware), Digital brightness, Digital contrast, Colour temperature, Digital saturation (software); the OSD icon is part of `OsdElement` now (`OsdLevelIcon`: Sun, Speaker, Disc, Thermometer; the red muted look follows the Speaker icon, not the label).

**Probing.** `IMonitor.Probe()` reads each code (about 11 reads) on a worker thread once per monitor object and keeps the answer; the cards show at once, greyed out ("Reading what this monitor supports..."), then fill in. The capabilities string (slow, many monitors answer it badly) is read only to list input sources and colour presets; if a monitor has the code but no list, that card stays greyed with "doesn't say which values it accepts". The probe gives up early (after three refusals in a row at the start) for a monitor with DDC/CI off. Sliders write through `LevelStepper.Set` (newest value only, on a worker thread), so dragging no longer blocks the UI on DDC writes (it did before); the raw value is a percent of the monitor's own maximum (`Percent.ToRaw`; before, brightness and contrast assumed a maximum of 100). The old `GetCapabilities` P/Invoke had the wrong signature and was replaced (`GetCapabilitiesStringLength` + `CapabilitiesRequestAndCapabilitiesReply`); the VCP code constants for colour gain, sharpness, saturation and colour preset were wrong in `VcpCode` and were corrected to the MCCS numbers.

**Self-refresh.** `ThrottledRun` (`Timing.DisplayRescanGap` 4 s; a request inside the gap is kept and runs when the gap ends) calls `MonitorEngine.Rescan()`, which only lists the attached displays (Windows' own data, no monitor is asked) and compares; only when the set changed are the monitors reopened and re-probed and the groups, search entries and actions updated. Triggers: page opened, `OnWindowShownChanged` (every page, whenever the window is shown, hidden, minimised, restored or closed), `OnDisplaysChanged` (`Window.Screens.Changed` -> `ShellViewModel.NotifyDisplaysChanged`, 1.5 s settle). Refresh / F5 forces a full reopen and re-probe. Expanded/collapsed state is kept by group key (`AppSettings.ExpandedGroups`); software slider values are kept for displays that stay; hardware sliders are re-read after a re-probe.

**Software safety.** A gamma ramp per display: `GetDeviceGammaRamp` before the first change gives the display's own ramp, so a calibration is kept (the changed ramp is laid over it). Ranges are limited so the screen cannot go black (`SoftwareColor`: brightness 40-100, contrast 50-150, temperature 3000-10000 K shown as warm/cool -100..+100 around 6500 K, no colour channel below 20 %). Windows may still refuse a ramp (`SetDeviceGammaRamp` returns false): the status line says so and the slider keeps its value. Every value starts neutral on every run and nothing is saved as a setting. Reset: "Reset everything" only (card "Reset all" and action `displays.sw.reset`; the per-display Reset card and its action `displays.sw.<device>.reset` were removed at the owner's request), program exit (`ShellViewModel.Dispose` -> `SoftwareDisplayService.Dispose`), and crash recovery: the original ramp of a changed display is written to `%APPDATA%\OmniControl\software-display.json` before the first change and written back at the next start-up. Saturation uses the Windows Magnification colour effect (`MagSetFullscreenColorEffect`): whole desktop only, labelled "(all displays)"; Windows is believed to remove the effect when the process ends (not tried), and it is also reset on exit. Magnification is only started when saturation is moved away from 100.

**Turn screen off.** VCP 0xD6 = 4 (the soft "off" a monitor leaves on a button press or input). The card's description, its tooltip and the page hint say how to get the screen back. Nothing turns it on automatically. Not tried on any monitor: a monitor that stops answering DDC/CI while off can only be woken by its own button.

**Not done / open.**
- Software gamma (the curve itself) and separate R/G/B sliders were left out (the job said optional); the colour temperature covers the tint. `RampMath` could take them.
- Display ids are Windows' display names (`\\.\DISPLAY1`), the same values the old device ids had, so saved device picks keep working, but Windows can renumber displays; `DisplayInfo.DevicePath` is available if a steadier id is wanted (it would drop saved device picks once).
- Windows' exact rule for refusing a ramp is not known here (it is said to refuse very dark ramps); extremes may fail with a message. Unverified on screen: that `Window.Screens.Changed` fires for a plugged monitor while the window is hidden to the tray, the inner-group indent and header look in both themes, the greyed-out look, how the Hardware header line wraps at the narrowest window.
- HDR, Night light and ICC profile loaders also use gamma ramps and can overwrite OmniControl's ramp (the picture then follows theirs until a slider is moved).
- Per-monitor OSD pop-ups show the control's name only (not which monitor).
- The Shortcuts and Mouse pages rebuild once per registered action, so a monitor with many controls causes about 25 rebuilds right after its probe; cheap now, batch the collection change if it ever shows.

## Displays compact: half-width cards, one Reset (12 Nov) - compiled only; nothing seen on screen
- Every card of the Hardware and Software groups is half width (`Half = true`, set where the Displays view-model's items make the cards). `SliderItem`, `ChoiceItem` and `ButtonItem` now implement `IHalfWidth`; their rows (`SliderRow`, `ChoiceRow`, `ButtonRow`) each got ONE shared half layout: label as `TextBlock.cardlabel` (two lines at the same height), then the slider (least width 64) and the value at the right / the drop-down (`ComboBox.choice.half` in `Theme.axaml`, fills the room instead of the centred 220 px) / the button (what it does is now its tooltip). A half slider does not show a `Companion`. `SubgroupPanel` and the Software group's own list use `HalfPanel`, so keyboard order (Up/Down by list order, Left/Right hopping between a pair's cards for multi-part cards) is the shared `RowEditing`/`GroupedCards` model; on a card whose only part is a slider or choice, Left/Right change the value as on any such card (use Up/Down to leave).
- Card pairs: Hardware (brightness, contrast) (red, green) (blue, saturation) (sharpness, speaker volume) (colour preset, input) (turn off); per display in Software (digital brightness, contrast) (colour temperature); group level (saturation, Reset all).
- The per-display "Reset" card and its action were removed (the owner wanted only "Reset all"). The per-display `SoftwareDisplayItem.Reset()` stays: Reset all uses it. No per-display reset hotkey now; saved keys on `displays.sw.<device>.reset` are simply gone (the action was one day old).
- `HalfPanel` pairs only when the list is at least 500 px wide (`MinPairWidth`); a Displays subgroup is narrower than a top-level list (indent and padding), so at the narrowest window (720) and probably at the default size the cards stack full width, with the half layout stretched. Lower `MinPairWidth` if the owner wants pairs there. Unverified on screen: label wrapping of "Saturation (all displays)", the half drop-down in search results (it is stretched in the editor column, least 300 px), the slider width in a half card.

## Displays: one action per control (12 Nov) - compiled only; nothing seen on screen, no monitor or display setting was changed

The per-monitor actions (ids `displays.hw.<device>...`, `displays.sw.<device>...`, one "Displays - Hardware - <monitor>" heading each) were merged with the older all-monitors ones. Now `DisplaysPageViewModel.Hotkeys.cs` registers once, at start-up, and never again when monitors come and go (the chooser only re-lists the monitors: `RefreshActionDevices`); `HotkeyRegistry.Unregister`, `HotkeyAction.Subgroup` and the per-monitor registration code were deleted.

- **Hardware** (heading "Displays - Hardware", scope chooser "All displays / pick displays", the laptop panel and DDC monitors listed): level up/down for brightness, contrast, red/green/blue gain, saturation, sharpness, speaker volume (ids `displays.<key>.up|down` with key brightness, contrast, red, green, blue, saturation, sharpness, speaker; gesture level id = key), `displays.preset.next`, `displays.input.next`, `displays.power.off`. A control acts on every chosen display that has it (`HardwareDisplayItem.Supports` / its availability); the rest are skipped silently. Input source: only a generic "Next input source" (cycles the monitor's own list). A named input ("Switch to HDMI 1") would exist for only some monitors and is gone. The pop-up shows the first display that reports a level.
- **Software** (heading "Displays - Software", chooser lists every attached display): digital brightness, digital contrast, colour temperature up/down (ids `displays.sw.<key>.up|down`, level ids `sw.brightness`, `sw.contrast`, `sw.temperature`); saturation (`displays.sw.saturation.up|down`, level `sw.saturation`) and `displays.sw.reset` stay global with no chooser. The pop-up shows the first display changed.
- **Saved data:** kept: `displays.brightness|contrast.up|down` and the gesture level ids `brightness`, `contrast`, `sw.saturation`, `displays.sw.saturation.*`, `displays.sw.reset`. Lost (the ids no longer exist, the entries stay unused in hotkeys.json / gestures.json): every `displays.hw.<device>.*` and `displays.sw.<device>.*` hotkey, and gestures set to a per-monitor level id (`hw.<device>.<key>`, `sw.<device>.<key>`), including the named-input hotkeys. The speaker volume level id is `speaker` (not `volume`, which is the Volume page's). Re-bind them on the Shortcuts page.
- **Page and search:** the Displays page is unchanged (per-monitor subgroups, half cards); its cards still search per monitor, now as "Brightness (<monitor model>)" under "Displays - Hardware" so the heading has no monitor part.
- **Planned follow-up (not done):** an "Add another shortcut" button on a hotkey card so one action can have a second key with its own device choice (for example one key for the laptop panel, another for an external monitor).
- Unverified: all of it on screen (Shortcuts headings and chooser, search, pickers, a hotkey on a real monitor, the OSD); a hotkey pressed before a monitor's probe has finished skips that monitor.

## Volume page: effects modes and compact sliders (12 Nov) - compiled only; nothing seen on screen, no audio device or setting was changed

Owner-approved mockup. (Superseded on 13 Nov by "Effects engine" below: the engine now runs the modes; the honesty lines were replaced by real status.)

**What exists.**
- **Master list** `App/Audio/FxMode.cs` (`FxModes`): Capture and replay (`capture`, programs and master), Virtual sound device (`virtual`, programs and master), Equalizer APO (`apo`, master only), Off (`off`). Each entry carries its names, a one-line description plus full `Details` (card tooltip), icon, tags (short text, full wording in the tag tooltip; Delay, Protected content, Exclusive-mode apps (not on APO), Setup) with a risk colour, where it is offered, its availability check, its missing text and its install/help page. Tag colours: Capture = delay caution, protected content risk (may go silent), exclusive-mode apps caution (bypass it), setup good; Virtual = delay caution, protected risk, exclusive good (usually work), setup caution (driver and admin); APO = delay good (lowest), protected good (works), setup caution (install and admin); Off = all good.
- **Availability** (`Core/Engine/Audio/AudioEffectsSupport.cs`, read only, off the UI thread through `App/Audio/FxModeService`, at most every 3 s (`Timing.FxModeRecheckGap`); asked at start-up, when the Volume page opens and when a pop-down opens): capture = Windows build 19041 or later; virtual = a playback device whose name contains one of VB-Audio Virtual Cable / VB-Audio Hi-Fi Cable / VB-Audio Cable (A-D) / VoiceMeeter / Virtual Audio Cable; APO = the key `HKLM\SOFTWARE\EqualizerAPO` (64-bit view) exists. Until the first answer every mode counts as available (no warning flash).
- **Row button** (`Pages/Volume/FxModeButton`, master pill variant `Button.pill.mode`): directly after the FX split button on the master and every program row; icon, short name (Capture, Virtual, APO, Off), chevron; accent tint while the pop-down is open; when the chosen mode is missing: yellow with a warning icon, tooltip "<Mode>: <missing text>. Playing without effects.", the row's second line reads "<Mode> missing: playing without effects", and the FX split button is greyed out (the shared `Availability` / `UserControl.card:disabled` look, reason as tooltip; it cannot be opened or switched until the mode is available or another mode is picked).
- **Pop-down** (a `Flyout`, presenter class `optionmenu`; fixed width `OptionMenuWidth` in `App.axaml` (400), no horizontal scroll, tags in a `WrapPanel`, small text via `TextBlock.small`/`Border.tag` 10.5): title "Effects mode for <program> (applies only to this program)" / "Effects mode for Master"; one `Button.optioncard` per mode (name, "Selected" tag on the current one, a yellow missing tag when unavailable, description, coloured `Border.tag` pills, and on a missing mode an Install pill that opens its page: VB-Audio Cable's page, Equalizer APO's SourceForge page, or Windows Update in Settings for capture); advice line; honesty line. Keys: the button is a row part (Left/Right), Enter/Space opens, Up/Down move between cards and wrap, Right/Left go to and from a card's Install pill, Enter picks and closes, Esc closes; focus returns to the button.
- **Stored** per entry in `mixer.json`: `AppAudioEntry.FxMode`, `MixerSettings.MasterFxMode` (id; empty or unknown = default, so old files load unchanged).
- **Hotkey** "Next master effects mode" (`volume.fxmode.next`, Volume heading): steps the master through its modes, wrapping; the footer names the new mode (and says when it is missing).
- **Inputs** have no effects mode (the program modes do not apply to recording devices): their rows keep the empty mode and pan columns so volume, mute and meter stay aligned with Outputs.
- **Compact sliders** (`Slider.compact` in `Theme.axaml`, used by `VolumeSlider` and the pan slider only): knob 13 px, track 3 px, as tall as the 26 px buttons; the Fluent track's see-through `FocusTarget` margin keeps a taller hit area for clicks on the track (the knob itself is 13 px). Pan column 60 px (slider and value), volume column 64-100 px (narrows in a small window), row column spacing 10 -> 6 px. Wheel, double-click reset, typing and keys are unchanged (they are not per control).

**Decisions.**
- Default for programs is **Off**, not Capture (the mockup showed Capture as an example): no audio engine exists, so defaulting to a capture mode would promise processing that does not happen. A program row shows "Off" until the user picks. The master's default is Equalizer APO as specified; on a PC without Equalizer APO the master row therefore starts yellow ("Equalizer APO missing: playing without effects") with its FX button greyed out until the owner installs it or picks another mode. Change `FxModes.DefaultFor` if that is not wanted.
- The FX split button's columns (`VolumeFx`) and the mode column (`VolumeFxMode`) are shared sizes inside the page's `Grid.IsSharedSizeScope`, so every row's FX frame is left-aligned in one column and every mode pill has the same width.
- New shared pieces: `ChevronDownIcon` (also used by the hotkey device picker now), `Palette.On / Caution / CautionInk / WashOpacity` (BrushOn now comes from `Palette.On`, same colour).

**Unverified (nothing seen on screen).** The compact slider template overrides (`Slider.compact /template/ Thumb`, `/template/ RepeatButton /template/ Border#TrackBackground`, `Grid#HorizontalTemplate`) assume the Fluent 11.3 template part names (found in the theme assembly, not read from its source); if the track does not thin, that selector is the place. Whether the row fits at 720 px is reasoned, not seen: by my estimate the name column keeps about 30 px there (it trims with an ellipsis); at the default 900 px the volume column is at its full 100 px. Both themes, the yellow/red washes on the light theme, the pop-down's width (300-440 px), the search-results view of a mixer row (its mode pill and pop-down), keyboard focus inside the pop-down (same pattern as the hotkey device list), the warning icon look, and the availability checks on the owner's PC (VB-Cable, Equalizer APO) were not tried.

**Planned next steps (not built).** 1) A Capture-and-replay prototype on one program (process loopback capture, effects, playback, muting the original only while it runs). 2) Equalizer APO for the master (write its config from the master chain). 3) The virtual sound device route. Each needs the audio engine first; until then keep the honesty lines.


## Engine research (licences, signing, sources) (12 Nov) - research only, nothing built or run, no project code touched

The full report (clone folders and commits) was a temporary file of that session and is not part of the repository (not a lawyer; licence readings are engineering readings of the real files).

**Recommendation.**
- **Licence for OmniControl: GPL-3.0-or-later.** Every GPL part found is "version 2 or later" (Equalizer APO headers, FFTW, libHybridConv), so GPL-2.0-only is not forced; GPL-3 also allows Apache/LGPL-3 pieces later. Avoid Qt and JUCE-AGPL in OmniControl. MS-PL driver code (SysVAD, Scream, parts of Virtual-Audio-Driver) is GPL-incompatible as a combined work: only as a separate driver package.
- **Master FX:** fork upstream **Equalizer APO** (SourceForge git, `bbfcc3e5024cbb9d61ba75fc88d78605cc4c9687`, 1.4.2, GPL-2.0-or-later) as "OmniControl APO": own CLSIDs, x64 only, no Qt; OmniControl writes its master chain as Equalizer APO config text and the APO live-reloads it. Needs a new command-line install helper (the upstream Device Selector is GUI only). The APO is unsigned and loads only because the installer sets `HKLM\...\Windows\CurrentVersion\Audio\DisableProtectedAudioDG=1` (breaks the protected audio path; the project says it will never be WHQL signed); admin + audio service restart; exclusive mode / ASIO bypass it; vendor drivers can overwrite its registry entries. Unknown: behaviour on Windows 11 24H2+/build 26300 and with Memory Integrity (owner to test on a spare PC).
- **Per-program FX, no driver:** process loopback (`NAudio.Wasapi` 3.3.0 `WithProcessLoopback`, MIT; Windows 10 build 20348 documented floor) + managed DSP + render to the real device, original app silenced via its session volume. **Make-or-break unknown:** is the capture taken before the session volume/mute (one developer says yes; unverified)? The owner runs a short spike before we build on it.
- **Virtual driver:** do not build one yet. Secure Boot + Windows 10 1607+/11 loads only Microsoft-signed kernel drivers; that needs an **EV code signing certificate + Hardware Dev Center (Partner Center) account + attestation signing**. SignPath Foundation (free for OSS) gives an ordinary Authenticode certificate and its terms do not mention kernel drivers: the SignPath-signed VirtualDrivers/Virtual-Audio-Driver 25.7.14 (`bb34fba15faf569a6ae9bdea360bc1cf4821354e`, MIT + MS-PL parts) is reported failing (Code 52) without test signing. Fallback cable: **VB-CABLE standard is redistributable "as is" with an app under its donationware terms** (correction: A+B/C+D are not; VAC is not); optional separate Install button.
- **VST3:** `NAudio.Vst3` 3.3.0 (MIT; managed host; stereo effects; in-process) on VST3 SDK 3.8.1 build 84 (`3cdf9ca5d1f5b1b21e0a86832aa4abe55607bd96`, now MIT; "VST is a registered trademark of Steinberg Media Technologies GmbH" wording). NAudio 3.x targets **net9.0**: OmniControl (net8.0-windows) must be retargeted first.
- **Components and versions:** Equalizer APO `bbfcc3e` (optional reference forks: TheFireKahuna/equalizerAPO64 `7156020e9b68b72f64d2bf47b0c15f178078b378` GPL-2.0, 64-bit doubles; Mixomo/EqAPO64_with_VST3_support `v3-rewrite` `db0d21310322ecd31caa8b16d27530fae219eefd` GPL-3.0, very young), FFTW 3.3.x, libsndfile (LGPL-2.1), muparserx 3.0.1 (BSD-2), NAudio 3.3.0 (commit `174bb499e5d36df1617377cbc1c72e503fc8f22e`, tag v3.3.0 `4f8889e2bd34c3978a25652c6c172fa1502ccba3`), MS ApplicationLoopback sample `434f6002bdf9cf9829406c3ff2b33387982d6168` (MIT).

**Credits list (About + NOTICE.txt):** Equalizer APO (GPL-2.0+, Jonas Thedering), FFTW (GPL-2.0+, Matteo Frigo / MIT), libsndfile (LGPL-2.1, Erik de Castro Lopo and team), libHybridConv (LGPL-2+, Christian Borss), muparserx (BSD-2, Ingo Berg), VeSTige aeffectx.h (GPL-2.0+, Javier Serrano Polo / LMMS, only if VST2 kept), NAudio (MIT, Mark Heath), Steinberg VST3 SDK (MIT, Steinberg Media Technologies GmbH), Microsoft ApplicationLoopback sample (MIT, if adapted), Avalonia and CommunityToolkit.Mvvm (MIT); if a driver is ever shipped: Microsoft SysVAD (MS-PL), Virtual-Audio-Driver (MIT, MikeTheTech), VB-CABLE (VB-Audio, donationware). Release duty: ship the licence texts and offer the exact source of every distributed binary.

**Open decisions for the owner.**
1. Licence choice: GPL-3.0-or-later (recommended) vs GPL-2.0-or-later; contributor terms (CLA/DCO).
2. Accept `DisableProtectedAudioDG=1` for the master engine (and a clear warning in the UI)? If not, the only alternative is an APO inside a Microsoft-signed driver package.
3. Who signs a driver: buy an EV certificate and open a Partner Center account (cost and eligibility for a private person unverified), or skip own driver and offer VB-CABLE? SignPath Foundation can sign OmniControl.exe/installer/APO DLL for free once the GPL source is public (every release manually approved).
4. Run the two spikes (per-program loopback + mute; Equalizer APO on a spare PC with Windows 11 26xxx) before the build starts (AGENTS.md rule 7 applies to the build agent, not to the owner).
5. Retarget to net9/net10 (needed for NAudio 3.x).
Build agent first steps: retarget framework; native APO fork with command-line install helper and a pinned dependency list; `Audio/Engine/` services (`MasterChainWriter`, `ProgramEngine`, `Vst3ChainHost`, `EngineInstaller`); credits page and `NOTICE.txt`; keep the Volume page honest ("not installed" until the engine is).

## Effects engine, licence and credits (13 Nov) - compiled only; nothing installed, registered, played or tested on the PC

Built from the owner's final decisions on the engine research (above). Checked with clean throwaway builds of the whole solution
(0 warnings, 0 errors) and two read-only runs of the APO helper's preview (`preview-uninstall`, `preview-install` against the
default playback device). Nothing else was run: no sound was captured or played, no session muted, no device routed, no registry
value written, no installer started.

**Framework.** The solution now targets `net9.0-windows10.0.19041.0` (Directory.Build.props and both csproj; NAudio 3.3.0 needs
net9). Only the .NET 8 and 9 SDKs are installed here (9.0.321); .NET 10 (LTS) is not, so net10 could not be used. Note: under
Microsoft's 24-month STS policy .NET 9 support also ends on 10 Nov 2026 (verify on Microsoft's lifecycle page); to move to .NET 10
install the .NET 10 SDK and change the three `TargetFramework` lines to `net10.0-windows10.0.19041.0`. Avalonia 11.3.6 and every
package build on net9. Build output is now `OmniControl.App\bin\Release\net9.0-windows10.0.19041.0\`. New packages (Core):
`NAudio.Wasapi` 3.3.0, `NAudio.Vst3` 3.3.0 (pulls `NAudio.Core`, `NAudio.Midi` 3.3.0, `System.Numerics.Tensors` 9.0.0),
`System.ServiceProcess.ServiceController` 9.0.0.

**Licence and credits.** `LICENSE` = the FSF GPL-3.0 text (SHA-256 of the file
`3972dc9744f6499f0f9b2dbf76696f2ae7ad8af9b23dde66d6af86c9dfb36986`, identical to gnu.org's gpl-3.0.txt; copied from the
research clone because gnu.org did not answer). `NOTICE.txt` = OmniControl's GPL-3.0-or-later notice, the VST trademark line and
the ONE credits list (Equalizer APO, FFTW, libsndfile, libHybridConv, muparserx, VeSTige, NAudio, VST3 SDK, Microsoft
ApplicationLoopback sample (design reference, no code copied), Avalonia, CommunityToolkit.Mvvm, TabForge, VB-CABLE). The app
embeds it and shows it in **App settings > About and credits** (new card; window with one card per project and a Website pill);
`LICENSE` and `NOTICE.txt` are copied next to `OmniControl.exe`. Upstream notices are unchanged in `native/EqualizerAPO`.
Copyright line used: "the OmniControl authors" (change in NOTICE.txt if the owner wants a name). No CLA/DCO was set up.

**How it is wired (engine overview).**
```
FX window / Volume row edits -> MixerService.SaveFx / SetFxOn / SetFxMode -> FxChanged ---------+
Volume row sliders -> MixerService.SetVolume/Pan/Mute/Device -> ProgramChanged ---------------+  |
FxModeService (what the PC has)  -> Changed ----------------------------------------------+   |  |
MixerService scan (every 2 s)    -> Updated ----------------------------------------------+---+--+--> EffectsEngine (UI thread)
   per program row, FX on + enabled effect + mode available + program playing:
     Capture and replay -> ProgramEngine: AudioPipe(process loopback of the program's process tree)
                           -> playback thread: mix -> ChainProcessor(gate, DspChain, row volume/pan/mute) -> program's device
                           original: MixerService.Silence (journal first), checked: recording must keep sound, else undone
     Virtual sound device -> CableRoute: MixerService.HoldRouting(program -> cable) (journal first)
                           -> AudioPipe(loopback of the cable) -> ChainProcessor(DspChain) -> program's own device
   master and microphones in Equalizer APO (Fast) -> ApoChains -> ApoChainWriter (one Device: section each) -> ApoConfigFiles:
                           <Equalizer APO config>\OmniControl.txt + one Include line in config.txt
                           -> the user's Equalizer APO (or OmniControl APO) in audiodg reloads it
   status per row -> EngineStatus -> row's second line, FX window line; failures -> footer
Install/Remove/Preview/Test on mode cards -> VolumePageViewModel.FxModes -> EngineInstaller / EffectsEngine.TestAsync
   Equalizer APO: used as installed; else its own installer (third_party\equalizerapo) or its page; Device Selector
   OmniControl APO (advanced, only when built): preview in-process / OmniControl.exe --engine-setup install|uninstall (administrator prompt)
   VB-CABLE: third_party\vbcable\VBCABLE_Setup_x64.exe (unmodified, administrator prompt) or vb-audio.com/Cable
Start-up: EngineJournal.Recover (before the mixer) puts back programs a crashed run left silenced or routed.
Exit: ShellViewModel.Dispose -> FX/editor windows close -> EffectsEngine.Dispose (every program back, OmniControl's line and file leave the APO config,
      plug-in states saved, plug-ins unloaded).
```
- Live changes: every chain edit (EQ band, wet, on/off, add/remove/move, plug-in), mode change and FX switch raises
  `FxChanged`; the engine takes the first edit at once and then at most one per `Timing.EngineChainApplyGap` (the last edit
  always lands), rebuilds the chain off the audio thread and swaps it in; the audio thread carries each slot's state over and
  changed values glide over `Timing.EffectGlide` (see "EQ crackle fix" at the end). Row volume/pan/mute act through `UserLevel`
  while the original is silenced.
- Audio thread rules: captures write into lock-free rings; the playback thread mixes, processes in blocks of at most 1024 frames
  with preallocated buffers, no locks and no allocation (`AudioPipe`, `DspStages`); NAudio's threads run at MMCSS "Audio".
  Delay is kept bounded: playback starts after 20 ms of sound, and more than 120 ms queued is dropped back to 20 ms (clock drift
  between two devices). Sizes in `CoreTiming.Pipe*`, timings in `Timing.Engine*/Capture*`.
- Safety: the journal is written before any session is touched or routed and removed after the undo; a pipe error, a capture
  that goes silent after muting, a throwing effect, the program closing, picking Off, switching FX off, exit: all put the
  original back first. A failed row stays stopped (status says why, footer reports it) until the user changes that row.
  OmniControl's own playback sessions are hidden from the Volume list.
- Master modes: Capture and replay and Virtual sound device were **removed from the master** (they cannot silence the master
  without silencing their own playback); the master offers Equalizer APO and Off. A stored master "capture"/"virtual" falls
  back to the default (Equalizer APO). (Tags were calmed on 13 Nov, see the section at the end.)
- APO mode writes only Equaliser (PK/LSC/HSC with Q, same RBJ formulas as the graph), Gain (Preamp) and IR Loader at 100 % mix
  (Preamp + Convolution); effects with Wet below 100 % or other kinds are written as comments and listed as "not run". The
  master row says "Using your Equalizer APO" (or "Using OmniControl APO"): OmniControl cannot see whether audiodg loaded it.
- On exit OmniControl's include line and `OmniControl.txt` are taken out (master effects run only while OmniControl runs, like
  the program engines). Change in `EffectsEngine.Dispose` if the owner wants them to stay.
- VST3: `Vst3ChainHost` loads a plug-in on first use (UI thread), restores `FxSlot.State`, saves it when its editor closes, when
  runs stop and on exit. "Plug-in window" in the FX window opens its own editor (`PluginEditorWindow`, a NativeControlHost child; since 14 Nov `EffectEditorWindow`, any effect).
  Mono and stereo effects (since the VST3 hosting test, 13 Nov); a plug-in that fails to load is listed as not run with the reason. Plug-ins run in OmniControl's
  process: a native crash ends OmniControl (the journal restores sound at the next start). A plug-in removed from a running
  chain stays loaded until every run has stopped (so the audio thread never meets a freed plug-in).
- Each effect in the FX window now says where it runs ("Runs in Capture and replay and Virtual sound device", "Saved only ...").

**Finding on the owner's PC (read-only preview, nothing changed):** upstream Equalizer APO is installed on the default playback
device "Speaker" (its classes are in that device's SFX/MFX slots) and DisableProtectedAudioDG is already 1. Since 13 Nov
OmniControl uses it directly (see the last section): the master card should show "Using your Equalizer APO".

**Native APO.** `native/EqualizerAPO` = upstream bbfcc3e without Editor, DeviceSelector, Benchmark, UpdateChecker,
VoicemeeterClient, Setup and Wiki; own class ids `{27AB1524-86BE-4216-B86B-EC533B2B90B6}` / `{7F3F0D64-5AAB-432D-943C-C0B715C12DA0}`,
key `HKLM\SOFTWARE\OmniControlAPO`, DLL `OmniControlAPO.dll`; changes listed in its README and noted in each changed file.
**Not built:** the C++ toolset (VS Build Tools 2022, MSVC 14.44, Windows SDK 10.0.26100 with the APO libraries) is installed,
but libsndfile, FFTW and muparserx 3.0.1 are not, and building them was not attempted in this job. `build.ps1` (not run) fetches
them from the pinned URLs with checked SHA-256 (listed in the README; muparserx 3.0.1 only exists as Equalizer APO's wiki
attachment) and builds x64 Release into `OmniControl.App\apo\`. Until then the app reports the APO as "Not installed" and the
preview says "The OmniControl APO is not built".

**VB-CABLE.** VB-Audio's licensing page was read on 13 Nov 2026 (raw page, not a summary). Its words: "We allow the distribution
of the VB-CABLE package AS IS together with your application (freeware or commercial software). VB-CABLE may also be embedded in
your installer and deployed through a silent installation process, provided that our donationware model remains applicable."
The model applies when the user "can clearly identify VB-CABLE as a VB-Audio application and has the opportunity to support the
project"; the application should mention "The origin of VB-CABLE: www.vb-cable.com" and "VB-CABLE is a donationware, all
participations are welcome". Professional settings must buy licences; VB-CABLE A+B / C+D cannot be bundled. So Install runs
`third_party\vbcable\VBCABLE_Setup_x64.exe` (administrator prompt) when the owner placed the unmodified package there
(`third_party/vbcable/README.md`), otherwise opens https://vb-audio.com/Cable/. Remove runs the same setup (its Remove Driver
button) or opens the page. The credit and donationware sentence are in NOTICE.txt / About and in the messages.

**Owner test steps (in this order; on the daily PC only steps 1-4).**
1. Build and launch as usual. App settings > About and credits: the list opens, Website pills open pages, Esc closes.
2. Volume page, expand Outputs. Master row: second line should say why the APO cannot run (see the finding above). Program
   rows: mode Off by default.
3. **Capture spike:** play music in a browser or Spotify. On its row: mode button > Capture and replay card > **Test** (keep it
   playing). Expect about 4-5 s: 0.7 s listening, ~1.5 s silence (muting check), maybe again with volume 0, then 2.5 s of the
   processed sound, then the original back. The card and the footer show the result: works with mute / works with volume 0 /
   fails both ways, and the extra delay in ms. Report the text. After the test the program's own volume, mute and pan must be
   exactly as before (check Windows' volume mixer too).
4. If the test worked: pick Capture and replay, add an Equaliser with a strong band, switch FX on. The row should go "waiting
   for sound" -> "checking" -> "Effects running · about N ms delay"; moving an EQ band must change the sound at once; the row's
   volume/pan/mute must still work; pick Off: the original sound returns at once. Close OmniControl while effects run: the
   program must sound normal after exit. Optional crash check: end OmniControl in Task Manager while effects run (the program
   stays muted), start OmniControl again: the program must come back at start-up.
5. VST3: add a VST3 effect in that chain, press Plug-in window, change something, close it, restart OmniControl: the setting must
   be kept. Try one plug-in at a time (an unstable plug-in can crash OmniControl).
6. VB-CABLE (spare PC preferred): place the package (third_party/vbcable/README.md), rebuild, Virtual sound device card >
   Install, restart Windows if asked, pick the mode on a program with FX on: the program goes to the cable, the processed
   sound plays on the program's device. Off / exit must put the program back on its device (check Settings > Volume mixer).
7. APO, **spare PC only** (Windows 11 build 26300, once with Memory Integrity on and once off): build the APO
   (`native/EqualizerAPO/build.ps1`), rebuild OmniControl, master row > Equalizer APO card (only on a PC without Equalizer APO)
   > **Preview changes** (read it), then **Advanced install** (short confirmation, administrator prompt, audio restarts). Then master FX on with an Equaliser: does the sound change?
   Does copy-protected playback (a streaming site) still play? Then **Remove**: the device's original effects, the
   DisableProtectedAudioDG value and the sound must be as before. Keep `%APPDATA%\OmniControl\effects-setup-log.txt` and
   `%TEMP%\OmniControlAPO.log` (audiodg's temp) for the report.

**Risks.** (1) The capture-mute question is still unanswered (step 3 answers it); if both ways fail, programs need the cable.
(2) Unsigned APO + DisableProtectedAudioDG on Windows 11 26300 / Memory Integrity untested; vendor audio software may rewrite
FxProperties; exclusive-mode apps bypass every mode. (3) VST3 in-process. (4) Delay of capture modes (estimate shown) may upset
video sync. (5) Per-program routing uses Windows' undocumented interface (`CoreAudio.Routing`); Windows remembers it per app, so
a crashed run's routing is undone only when that program is seen again (the journal keeps it until then). (6) Process
loopback documented minimum is build 20348 (the app allows 19041 as NAudio does). (7) The capture format (48 kHz float, then
16-bit) is not confirmed against the process loopback device on this PC.

**Open items.** Port compressor, limiter, delay, gate, reverb and IR convolution to the program engine; out-of-process VST3
hosting; build the APO and decide whether it should keep running after OmniControl exits; processing-mode values the APO
install adds to a device are left on uninstall (the originals of the effect slots and of "enhancements off" are restored);
the Apply step of the helper restarts the audio service with a 30 s wait per direction; the old bare `catch` blocks in
`MixerService` (`NameOf`, `Scan`) are still to be narrowed; `MessageWindow` is not modal; plug-in loading happens on the UI
thread (a slow plug-in can pause the window for a moment); the About window and message window were not seen on screen
(rule 6 checks of the narrowest window and both themes not done).

## Equalizer APO: use the owner's own, setup without a second program, calm wording (13 Nov) - compiled only; nothing installed, written to Equalizer APO's folder, played or run

Owner feedback: pressing Install on the master "Equalizer APO" card showed "OmniControl APO can't be installed", the long warning
and a raw .NET dump: `FileLoadException ... OmniControlApoSetup.dll ... An Application Control policy has blocked this file
(0x800711C7)`. Owner: the install must work here, an Equalizer APO that is already installed must simply be used, and the
warnings were far too alarming.

**Root cause of the block.** Install first ran a preview: `HelperProcess.RunAndRead("OmniControlApoSetup.exe", "preview-install ...")`
(not elevated). That apphost started and asked the .NET host to load `OmniControlApoSetup.dll`, a second, new, unsigned assembly
built by the separate `OmniControl.ApoSetup` project; Windows App Control (Smart App Control here; 0x800711C7 =
ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION, 4551) refused that file, the host printed the exception to stderr, and the app put the
captured stderr into the dialog. OmniControl.exe and its own DLLs evidently pass (the app runs). Why Smart App Control judged the
helper differently is not provable from here; the fix avoids needing any further unknown file.

**What changed.**
- **No second program.** The helper project `OmniControl.ApoSetup` is deleted (solution, project reference). Its code moved into
  `App/Audio/Engine/EngineSetupCommand.cs`: `OmniControl.exe --engine-setup preview-install|install|preview-uninstall|uninstall
  [--device <endpoint id> --source <folder>] --log <file>` runs from `Program.Main` before Avalonia starts and exits (exit codes
  0 done, 1 plan problems, 2 step failed (an install is undone), 3 not administrator, 4 bad arguments; every run writes "OmniControl
  engine setup started" to the log first, and unexpected exceptions go to the log). Install/Remove of OmniControl APO start the
  running `Environment.ProcessPath` again with the administrator prompt; the preview now runs in-process (registry reads only).
  `HelperProcess` lost `RunAndRead` and reports `HelperFailure` (Cancelled, Blocked by App Control / AppLocker, NotStarted,
  StillRunning). If even the elevated OmniControl.exe were blocked, the user gets one line: "Windows blocked OmniControl's setup
  step (Smart App Control); use Equalizer APO's own installer instead."
- **The owner's Equalizer APO is used as it is.** `ApoStatus` also reads upstream Equalizer APO (`HKLM\SOFTWARE\EqualizerAPO`:
  installed, its `ConfigPath` folder, its `DeviceSelector.exe`; whether its classes are on the default device). Master mode
  "Equalizer APO" is available when upstream Equalizer APO is on the default device, DisableProtectedAudioDG is 1 and its config
  folder exists (else when OmniControl APO is fully installed). `ApoConfigFiles` writes the master chain (same
  `MasterChainWriter` translation: Equaliser, Gain, IR Loader) to `OmniControl.txt` in that folder and adds ONE line pair at the
  very top of `config.txt`: `# Added by OmniControl for its master effects; OmniControl removes it when they stop.` and
  `Include: OmniControl.txt` (at the top so none of the user's `Device:`/`Channel:` selections apply to it; Equalizer APO restores
  channel selection after an include). Equalizer APO watches its config folder (FindFirstChangeNotification, sub-tree,
  file name + last write) and reloads, so EQ edits apply live by rewriting only `OmniControl.txt`. When master effects stop (Off,
  FX off, nothing enabled, mode missing, exit) and at start-up (cleans what a crashed run left) the two lines are taken out and
  `OmniControl.txt` is deleted; nothing else in the file is touched (bytes are kept exactly, UTF-8 BOM respected). Before the first
  change `config.txt` is copied to `config.txt.omnicontrol-backup`; on removal that copy is deleted when the file matches it again,
  and kept (the user's edited file is NOT overwritten) when the user changed `config.txt` in between. No administrator rights:
  Equalizer APO's own installer grants the Users group full access to its `config` folder (`Setup/Setup.nsi`:
  `AccessControl::GrantOnFile "$INSTDIR\config" "(S-1-5-32-545)" "FullAccess"`), so the elevated path is not used for this. A
  folder that is not writable gives the row status "Couldn't write to Equalizer APO's config folder (...)" (a failed clean-up is
  said once in the footer). Writes use no file sharing (Equalizer APO waits on a busy file) with up to 5 tries 10 ms apart
  (`Timing.ApoFileRetry`/`ApoFileAttempts`).
- **Card and setup buttons.** The master card shows a green "Using your Equalizer APO" tag (or "Using OmniControl APO") and the
  row's status says the same (`FxMode.InUse`, read through `FxModeService.InUse`). Missing reasons are now: "Not installed",
  "Not on this device" (button **Choose devices** runs Equalizer APO's Device Selector elevated), "Not loaded by Windows"
  (DisableProtectedAudioDG not 1), "Config folder not found", "Install incomplete". With no Equalizer APO at all, **Install** runs
  the official installer if the owner placed it in `third_party\equalizerapo` (new README; copied next to the app; git-ignored),
  else opens the SourceForge page. OmniControl APO is an advanced extra: **Advanced install** + **Preview changes** only when its
  DLL was built into `apo\` and no Equalizer APO is installed; **Remove** only when it is installed.
- **Failures.** Every setup step returns `SetupResult` (one plain sentence; when it failed, the technical detail). A failure opens a
  small window: the sentence, "Details: %APPDATA%\OmniControl\effects-setup-log.txt", and **Copy details**. No exception text is
  shown in a dialog any more. The log file was `apo-setup-log.txt`; it is now `effects-setup-log.txt` and covers every setup step.
- **Calm wording.** `MessageWindow` is now small (460 px wide, height to content): title, two or three lines, Install/Remove
  (`Button.usebtn`) + Cancel (`Button.primary`), Copy details (`Button.pill`); a details box only for "Preview changes". The long
  DisableProtectedAudioDG/DRM/anti-cheat/Blu-ray warning is gone; confirmations say what changes, that some protected apps may not
  play, the administrator prompt and how to undo it. Using an existing Equalizer APO shows no dialog at all. Tags (one master table,
  `FxModes`): `FxRisk` is now Good (green) / Info (plain) / Caution (yellow); no mode tag is red. Equalizer APO: "Lowest delay",
  "Some protected apps may not play" (yellow); Capture: "30-80 ms delay" (plain), "Nothing to install"; Virtual: "30-80 ms delay",
  "Uses VB-CABLE" (plain), "Some protected apps may not play" (yellow); Off: "No delay". The repeated "unaffected" and
  "exclusive" tags moved into the card tooltips. AGENTS.md got design principle 11 "Calm wording".
- Tidied: mojibake "Â·" / "â€¦" in visible strings (row status "Not open · settings saved", EQ graph label, FX window hint, "Testing…",
  "Starting effects…") fixed; unused `FxInstall.CanRemove` and `FxTag.IsRisk` removed; `MasterChainWriter.Write` replaced by
  `ApoConfigFiles`.

**Owner test steps (daily PC is fine: OmniControl only adds and removes its own line).**
1. Build and launch. Volume page > Outputs > master row > mode button: the "Equalizer APO" card shows "Using your Equalizer APO"
   (green) and no Install button.
2. Pick Equalizer APO for the master, open its FX window, add an Equaliser with a strong band (e.g. +12 dB at 100 Hz), switch the
   master FX on. Play music: the change must be heard within about a second; the row says "Using your Equalizer APO". Move the band:
   the sound follows. Open `C:\Program Files\EqualizerAPO\config\config.txt` in Notepad: the first two lines are OmniControl's
   comment and `Include: OmniControl.txt`; the rest is your own text, unchanged.
3. Pick Off (or switch master FX off): the two lines and `OmniControl.txt` disappear and your own EQ is as before. Switch back on,
   then close OmniControl from the tray: the lines disappear again. `config.txt.omnicontrol-backup` should be gone when the file is
   back to its original.
4. Optional: with master effects on, end OmniControl in Task Manager (the line stays), start it again with master FX off: the line
   is removed at start-up.
5. If anything fails: the small window's Copy details and `%APPDATA%\OmniControl\effects-setup-log.txt`.

**Unverified / risks (13 Nov, before the fix below).** Nothing was run: Equalizer APO's reload of an included file is from its source (folder watcher), not seen;
the Equalizer APO Configuration Editor may rewrite `config.txt` and drop OmniControl's comment line (the include line is still
found and removed by its text); if the user removes the include line while effects run, it comes back only at the next chain
change; both the user's own filters and OmniControl's run (OmniControl's first), so two Preamps add up; a backup kept because the
user edited `config.txt` stays until deleted by hand; the elevated self-start was not tried, so whether Smart App Control
allows the elevated OmniControl.exe is reasoned (same files as the running app), not seen; the Equalizer APO installer waits for its
Device Selector, so OmniControl stops waiting after 2 minutes and says it is still open. The message window, the green tag and the
new tags were not seen on screen (rule 6 checks of the narrowest window, both themes and search results not done).

## Equalizer APO: real-install detection, Device Selector's checks, reinstalled on this PC (13 Nov) - compiled only (throwaway build); app not run

Owner screenshot: master card "Equalizer APO" said "Not on this device", and Choose devices said the Device Selector wasn't found.

**Root cause.** Equalizer APO was not installed any more. `C:\Program Files\EqualizerAPO` held only `config` (the owner's
config.txt + stock examples; the folder was last changed 13 Nov 08:11, i.e. an uninstall removed the program files); no
DeviceSelector.exe / EqualizerAPO.dll, no Uninstall entry, no COM classes, no class on any device, DisableProtectedAudioDG
absent. Only `HKLM\SOFTWARE\EqualizerAPO` (InstallPath, ConfigPath, EnableTrace, Start Menu Folder "Equalizer APO 1.4.2") and the
config folder were left. `ApoStatus` counted the key alone as "installed", so the card said "Not on this device" and offered a
Device Selector that did not exist. No half-removed registration was found (no Equalizer APO or OmniControl class in any render
or capture FxProperties, no CLSID / AudioEngine entries); the Speaker's own Realtek APOs (composite slots 13/14/15) were
untouched; AudioSrv and AudioEndpointBuilder were running. So nothing was undone. The browser's "Audio renderer error" was not
explained by any registration found here.

**What changed (code).** `ApoStatus` now uses Equalizer APO's own Device Selector checks (`native/EqualizerAPO/DeviceAPOInfo.cpp`):
installed = `EqualizerAPO.dll` in its InstallPath (key/config leftovers do not count); registered = both classes under CLSID and
AudioEngine\AudioProcessingObjects (checkAPORegistration; also used for OmniControl APO's `Registered`); "audio enhancements off"
on the default device (enhancementsDisabled); DisableProtectedAudioDG = 1 (checkProtectedAudioDG); on the device = its class in
LFX/GFX/SFX/MFX/EFX. `UsesUpstream`/`Ready` need all of them. New card reasons: "Not registered", "Audio enhancements are off"
(both fixed in its Device Selector, so the card offers **Choose devices**); with leftovers only the card says "Not installed"
with **Install** (installer in `third_party\equalizerapo`, else https://sourceforge.net/projects/equalizerapo/). An installed
Equalizer APO with no config folder offers Install (reinstall). `ApoIds` got `UpstreamDllName`, `ApoRegistrationKey`,
`ClsidKey`, `DisableEnhancements` (moved from `ApoPlanner`); `ApoPlanner` keeps DisableProtectedAudioDG on uninstall only when
Equalizer APO is really installed. The leftover config folder is still read so OmniControl can take its own include line out.

**Device Selector / installer checks and where OmniControl does them.**
- Protected audio path (checkProtectedAudioDG): `ApoState.ProtectedAudioDisabled` -> "Not loaded by Windows". Done before.
- APO registration (checkAPORegistration, CLSID + AudioEngine): now `UpstreamRegistered` / `Registered` -> "Not registered". New.
- Audio enhancements off on the device (enhancementsDisabled; its install force-enables them): `EnhancementsOff` -> "Audio
  enhancements are off"; the fork's planner already turned them on and back off on uninstall. Status new, planner done before.
- Device state (not present skipped, disabled, unplugged): the master always uses the active default device. Not needed.
- Slot layout (LFX/GFX only when the driver uses only those, combined Bluetooth -> SFX/MFX, else SFX/EFX; children kept;
  processing-mode values): `ApoPlanner.LayoutOf` / `Children`, done before.
- Already on the device / existing registrations: planner refuses a second APO on a device. Done before.
- Install layout version ("2"; newer = refuse, older = upgrade): only Device Selector writes Equalizer APO's Child APOs; the
  fork writes "2". Not checked for upstream (Device Selector does it itself).
- Device without FxProperties ("experimental"; Device Selector takes ownership and creates the key): the fork's planner refuses
  instead. Different on purpose, open item.
- testAPOInstallation (opens the device in shared mode after install): done by Device Selector for Equalizer APO; NOT done for
  the fork's install. Open item.
- 32/64-bit: only x64 installer names are offered (`EqualizerApoSetupNames`); the app itself is x64. Done before.
- Restart needed: installer/Device Selector messages say a restart may be needed. Done before.

**On this PC (owner's permission for this job).** config.txt backed up to `%APPDATA%\OmniControl\EqualizerAPO-config.txt.backup-20261113`
(hash identical). Official installer `EqualizerAPO-x64-1.4.2.exe` (current release, 11,980,366 bytes, SHA-256
7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa = Chocolatey's published checksum for the SourceForge file; the
installer is not Authenticode-signed upstream) downloaded from downloads.sourceforge.net, copied unchanged to
`third_party\equalizerapo\` (git-ignored) and run elevated with `/S` (owner approved the administrator prompt). It installed the
program files, registered both classes, set DisableProtectedAudioDG = 1, added its Uninstall entry and opened its Device Selector.
config.txt was not changed (same hash as the backup).

**Owner steps.** In the open "Equalizer APO 1.4.2 Device Selector": tick **Speaker** (the default playback device), leave the
options as they are, OK, then restart Windows when asked (or at least restart audio apps). Then build/launch OmniControl: the
master card should show "Using your Equalizer APO".

## Choose devices: Qt plugin error fixed; Smart App Control blocks new OmniControl.Core.dll (13 Nov) - fix compiled only; flow not run

**Qt error, root cause.** Choose devices showed "This application failed to start because no Qt platform plugin could be
initialized". Equalizer APO's Qt programs have no `qt.conf`; they find their `qt\platforms` folder relative to the *current*
folder, not their own. `HelperProcess.RunElevated` started them elevated with no working folder, so they ran in
`C:\Windows\System32`. Reproduced without elevation (RunAsInvoker, nothing installed or changed): DeviceSelector.exe from
System32 shows exactly that dialog; from its install folder it opens normally. The inherited environment is not the cause (the
same PowerShell environment worked from the install folder). **Fix:** `RunElevated` now sets `WorkingDirectory` to the
program's own folder, so every program OmniControl starts elevated (Device Selector, Equalizer APO's installer, VB-CABLE setup,
OmniControl's own setup mode) runs in its folder. An environment cannot be passed through the administrator prompt
(ShellExecute); none is needed.

**Smart App Control (enforcing on this PC).** CodeIntegrity events 3033/3077/3118 for every blocked start: "did not meet the
Enterprise signing level", Smart App Control block, no Mark-of-the-Web on the files. Builds 130057 and 130329 produced the
same Core.dll (SHA-256 16678E8A...) and both were blocked; the older Core.dll (2CFE2C05..., builds 124412/124945) loads; a
fresh unsigned `OmniControl.dll` (App) loads fine.

**Bisect result: no code triggers it; the verdict is per file hash and random with respect to the code.** Evidence (same day,
loading each DLL into a fresh PowerShell with `Assembly.LoadFrom` and reading the CodeIntegrity log):
- Rebuilding the blocked source (current source minus the `WorkingDirectory` line) gave a DLL whose types, methods, IL sizes,
  strings, member references and P/Invokes are identical to the blocked one; only 72 bytes differ (PE timestamp, MVID, PDB
  id: the doc comment edited in `HelperProcess.cs` changes the PDB checksum). That DLL is **allowed**.
- Eight rebuilds of the unchanged current source with `--no-incremental -p:Deterministic=false` (identical code, new MVID and
  hash each time): 6 allowed, 2 blocked. Each verdict is sticky for its hash (re-tested: same result).
- The current source with the working-folder fix builds deterministically to Core.dll F3C8030E..., allowed every time.
So there is nothing "suspicious" in today's Apo/HelperProcess code to remove: identical code is accepted or refused depending on
the hash alone. Nothing was changed in the code for this. **What to do when it happens again:** any rebuild that changes the
file (a real source change, or one build with `-p:Deterministic=false`) gets a fresh verdict, usually allowed; check with
`[System.Reflection.Assembly]::LoadFrom(<dll>)` in a new PowerShell (0x800711C7 = blocked). The lasting fix, also for other
PCs, is signing the output with a certificate Windows trusts (for example Azure Trusted Signing; self-signed does not count), or
reporting a wrongly blocked file to Microsoft; turning Smart App Control off is the owner's call, never an agent's.

**Checked on this PC (read only).** AudioSrv and AudioEndpointBuilder running, no audio service errors in the System log for
the last 8 h, Equalizer APO is on no playback device: the browser's "Audio renderer error" is not caused by OmniControl or
Equalizer APO. config.txt unchanged (same hash as the backup).

**Not verified.** Build `Relaunch131547` (with the working-folder fix) loads and was launched; it was still running and
responding after about 15 s. Choose devices was not pressed with the fix, Device Selector was not driven and the card was not seen to say "Using your Equalizer APO". That the elevated start honours the working folder
is from Windows' documented ShellExecuteEx behaviour plus the repro, not seen through the administrator prompt.

## EQ crackle fix: glides, safety limiter, APO headroom (13 Nov) - compiled only (throwaway build); nobody listened, app not run

Owner report: crackling and hissing when changing the EQ, worst with extreme values. Program engines (Capture and replay,
Virtual sound device) and the master APO line were checked; Equalizer APO is on no device on this PC, so the program engine is
what was heard.

**Causes found in code.**
- Confirmed: a band at 0 dB was left out of the built EQ (`Gain != 0`), so the number of filters changed whenever a band
  reached or left 0 dB or a preset changed. State was only carried when the count matched, so the filters restarted from zero
  (a click); when the count matched but a different band had dropped out, each band got another band's state (worse click).
- Confirmed: new coefficients took effect on the next sample: a jump from -12 to +12 dB is an instant step in the filter.
- Confirmed: no headroom or limiter. Boosts of +6 to +12 dB on normal music go past full scale and clip in the device's
  conversion (crackle/hiss), unless the row volume brings it back down. Same on the master APO line: only the bands were written.
- Confirmed: an EQ drag sent one chain rebuild per pixel (and, for the master, one config-file write per pixel, each making
  Equalizer APO reload). `mixer.json` is also saved on every pixel (UI thread only; left as is, see open items).
- Confirmed (likely high CPU): biquad state decays toward zero in silence and after roughly 15 s becomes denormal (very slow on
  x86); a NaN in the state would also have stayed for good.
- Checked, not a cause: the chain swap is one reference write and read (no lock); stages are built off the audio thread; state
  carry-over copies into preallocated arrays. Each rebuild still makes garbage for the GC (now at most 25 per second).
- Stages were matched to the old chain by position, so adding or moving an effect handed one effect's state to another.

**Changes (shared DSP code, once).**
- `EqStage` keeps every band in its place (a 0 dB band is exactly `BiquadCoefficients.Identity`, from `EqMath.Coefficients`, and
  is skipped once settled, so CPU is as before for flat bands). On an edit each band's coefficients move in a straight line to the
  new ones over `Timing.EffectGlide` (30 ms) from wherever the old stage was, mid-glide included; a straight line between two
  stable biquads stays stable. State below 1e-20 or non-finite is set to zero after each block.
- `Glide` (same file): the same straight-line move for Gain and Wet. Every effect is now wrapped in a `WetStage` (at a settled
  100 % only the effect runs, sound identical), so moving Wet across 100 % keeps the effect's state.
- `DspChain` matches stages by the slot they were built from, not by position.
- `PeakLimiter` (new, last step of `ChainProcessor`): output kept below -1 dBFS (`PeakLimiter.CeilingDb`), stereo-linked,
  instant attack, release `Timing.LimiterRelease` (150 ms); below the ceiling the gain is exactly 1 (sound unchanged); a NaN or
  infinite sample becomes silence. Not shown in the UI: with big boosts on loud music, peaks are turned down (heard as the
  boosted sound not getting louder past a point, maybe a little pumping), instead of clipping.
- `EffectsEngine.OnFxChanged`: the first edit applies at once, then at most one per `Timing.EngineChainApplyGap` (40 ms); the
  last edit always lands. Also limits APO config writes.
- Master APO (`MasterChainWriter`): the EQ's bands now follow `Preamp: -X dB`, X = the EQ's highest boost (`EqMath.PeakDb`,
  log grid plus band frequencies), as Equalizer APO advises; nothing when every band cuts. **The master gets quieter by X dB**
  while it boosts; shown only in OmniControl.txt (the APO has no limiter, so this is the only way to stop clipping there).

**Not verified.** Nobody listened; the app was not run; only a clean compile. Not measured: CPU before/after, whether 30 ms
glides are short enough to feel instant while dragging, how the limiter sounds on heavy bass boosts.

**Owner test steps.** Capture and replay (or Virtual sound device) on a music program, Equaliser in its chain, FX on:
1. Play loud music. Drag the 62 Hz band from 0 to +12 dB and back quickly, several times: no clicks; the bass should not crackle
   at +12 (it may stop getting louder near the top: that is the limiter).
2. Preset Flat -> Bass boost -> V shape -> Treble boost -> Flat, quickly: no clicks.
3. Drag one band slowly across 0 dB (to -12 and back to +12): no click at 0.
4. Set several bands to +12 (Custom), play loud music for a minute: no hiss or crackle; then pause the music for 30 s with FX on
   and check OmniControl's CPU in Task Manager stays low.
5. Wet slider from 100 to 50 and back; effect on/off; move the EQ above/below a Gain effect: no lasting noise.
6. Master (only on a PC with Equalizer APO on the device): boost a band +6 dB, open `OmniControl.txt` in Equalizer APO's config
   folder: a `Preamp: -6 dB` line comes first; the level drops by about that much.

**Open items.** Turning a whole effect on or off, adding or removing one, is still an instant change (no fade in/out); a VST3
plug-in's own parameters are not glided by OmniControl. `MixerService.SaveFx` writes `mixer.json` on every drag step (UI thread
only); saving could be rate limited like the engine. The limiter has no look-ahead (a lookahead would trade a few ms of delay
for cleaner peaks).

## Equaliser redesign, ReaEQ-style (13 Nov) - compiled only (throwaway build); nobody listened, nothing seen on screen, app not run

Layout and way of working copied from REAPER's ReaEQ (owner's screenshot); no ReaEQ code (closed source). Maths: our own RBJ
Audio-EQ-Cookbook biquads (`Audio/EqMath.cs`).

**What it is now.**
- Model (`Audio/Eq.cs`): any number of bands (soft cap `Eq.MaxBands` = 64, only to bound the tabs and the engine's fixed arrays;
  CPU is not the limit). Each band: id (stable, so the engine keeps its state), Enabled, Type, Frequency, Gain, Q. Types: Low shelf,
  High shelf, Band (peaking), Low pass, High pass, All pass, Notch, Band pass. Gain -24 to +24 dB; Q 0.1 to 150, shown also as
  bandwidth 0.01 to 6.7 octaves (one Q, converted by the cookbook formula `1/Q = 2 sinh(ln2/2 * BW)`). Pass, notch, band-pass and
  all-pass ignore gain (as Equalizer APO does): their gain control is greyed out. A new EQ has 4 flat bands (low shelf 100 Hz, bands at
  400 Hz and 2.5 kHz, high shelf 8 kHz). Overall gain (-24 to +24 dB, whole dB) is the EQ kind's one parameter and an ordinary
  slider card under the editor.
- Engine (`Audio/Engine/Dsp/EqStage.cs`, moved out of `DspStages.cs`): all eight types; state matched by band id; edits glide over
  `Timing.EffectGlide` as before; a new band fades in from flat, a removed band fades out in a spare slot (no click); overall gain
  glides. 0 dB identity, flushing and the limiter are unchanged.
- Master APO (`MasterChainWriter`): one `Filter: ON PK|LSC|HSC|LPQ|HPQ|BP|NO|AP Fc .. Hz [Gain .. dB] Q ..` line per band that
  changes the sound, after ONE `Preamp:` = overall gain minus the bands' highest boost. LP/HP are written as LPQ/HPQ, the forms that
  take a Q (checked against the fork's `BiQuadFilterFactory.cpp`). Frequencies have at most two decimals, because Equalizer APO reads
  a point four places from the end ("100.123") as a thousands separator. Both engines run every type, so nothing new is "not run".
- Loading older settings: `MixerSettings.Load` runs `Eq.Upgrade` on every EQ slot (master, programs, recording devices): bands keep
  their types, frequencies, gains and Q exactly (so they sound the same) and only gain ids; values outside today's ranges are held
  inside them. Old single-EQ files still convert as before. Presets: Flat = the new 4-band default; Bass boost, Treble boost, Vocal,
  V shape, Loudness, Night keep their 10-band shapes (same sound) and are still recognised by name. A saved 10-band all-flat EQ now
  shows "Custom" (it is not the new Flat).
- Editor (`Fx/EqPanelItem.cs`, `EqBandItem.cs`, `EqGraph.cs`, `EqPanelRow.axaml`): graph (-24..+24 dB scale, grid every 6 dB,
  numbered points: drag = frequency and gain, wheel over a point = bandwidth by one slider notch; dashed phase curve and a degree
  scale when Show phase is on), wrapping band tabs (number + gain, or the type for gain-less types, "Off" when bypassed), the
  selected band's rows (title + Type drop-down + On switch; Frequency with Hz and note name; Gain; Bandwidth with octaves and Q),
  pills Add band / Remove band / Reset defaults, switches Show tabs / Show grid / Show phase (saved in settings.json, shared by every
  equaliser). Every value: slider (drag, Left/Right after Enter, wheel, double-click resets to that place's default), typing by
  double-click on the value or Ctrl+Enter on the slider (Hz 1 decimal, dB 1, octaves and Q 2). Left/Right on the card still pick
  the band.
- Shared pieces changed once: `NumberText` takes doubles and a `Decimals` count (whole numbers as before when 0);
  `TypedNumber.TryRead` gained a decimals overload and `Show`; `ValueWheel.StepOf` is the one notch size; the master
  `Button.scope.band` style got horizontal padding and a least width so wrapping tabs read evenly.

**Not verified.** Nobody listened; the app was not run; no window was seen (light or dark, narrow or wide); no `mixer.json` from the
owner's PC was loaded. The phase curve is drawn wrapped (-180..180), so it jumps at the wrap like ReaEQ's. Not checked: whether the
tabs, the 220 px type drop-down and the value columns fit in the narrowest FX window without crowding the sliders.

**Owner test steps.**
1. Open an existing program or master chain with an Equaliser: its bands, gains and preset name are as before; it sounds the same.
2. Add a new Equaliser: 4 flat bands. Add band until 6; drag points; wheel over a point (bandwidth changes, octaves and Q move together).
3. Type values: double-click the Hz, dB, octaves and Q texts; Ctrl+Enter on each slider types the value on its own row; Esc cancels.
4. Each type: pick Low pass, High pass, Notch, Band pass, All pass (gain greys out), shelves and Band; the curve follows; play music
   with FX on and switch types and remove/add bands while playing: no clicks.
5. Show phase on: dashed curve with degree labels; Show grid off; Show tabs off (points still select bands). Reopen the window: kept.
6. Overall gain card: the curve moves up/down; Reset defaults returns to 4 flat bands.
7. Master on a PC with Equalizer APO: open `OmniControl.txt`: one Preamp line, then one Filter line per non-flat band with its type.
8. Both themes, the narrowest FX window.

**Open items.** Overall gain moves in whole dB (it is an ordinary slider card; half-dB would need decimals in `SliderItem`). The
band tabs are not keyboard stops (Left/Right on the card picks the band instead). A removed band whose fade-out finds every spare
slot busy (more than 64 removed within 30 ms) would stop at once.

## VST3 hosting test with 20 free plug-ins (13 Nov) - run on this PC with the owner's permission; fixes built and run

The owner allowed using the PC (build, run, UI Automation, quiet test sound) and downloading free VST3s for this job. The PC was
locked the whole time (screenshots came out black), so everything was driven and read through UI Automation, plus two scratch
programs that load each plug-in the way OmniControl does (one plug-in per process; and OmniControl's own built chain code,
`ChainBuilder` + `ChainProcessor` + `Vst3ChainHost` from the built `OmniControl.dll`, on a test tone). Downloads (official pages /
GitHub releases only, each unzipped into its own scratch folder, nothing installed): TDR Nova 2.2.3 and TDR Kotelnikov 1.6.7
(tokyodawn.net "no installer" zips), Airwindows Consolidated 2026-11-08 (GitHub baconpaul/airwin2rack, SHA-256 matches GitHub's
digest), Surge XT 1.3.4 plug-ins-only zip (GitHub surge-synthesizer/releases-xt; size matches, no hash published), Dragonfly Reverb
3.2.10, DPF-Plugins 1.7, Wolf Shaper 1.0.2 (GitHub), nih-plug "latest-builds" 2026-09-03 (GitHub robbert-vdh/nih-plug, SHA-256
matches). Not used: Graillon (Windows only as an installer; its Linux bundle served as a "no Windows version" test), Neural Amp
Modeler (installer only), ZL Compressor (MSI only), Voxengo and Valhalla (installers only). Test copies sat in
`%LOCALAPPDATA%\Programs\Common\VST3\OmniControlTest` (scanned by the app's own Scan button) and were removed afterwards;
`mixer.json` was put back from a copy taken before the test and the `plugins.json` the test created was deleted.

**Results.** Scan: 19 found, the 32-bit TDR Nova and the Linux-only bundle left out (after the fix). Loaded and processed through
OmniControl's chain (with the ReaEQ-style Equaliser first, several plug-ins together, chain rebuilt and reordered mid-run, no
NaN, 9-16x faster than real time): TDR Nova, TDR Kotelnikov, Airwindows Consolidated, Surge XT Effects, Dragonfly Hall Reverb,
MaPitchshift (mono input), Wolf Shaper, Spectral Compressor, Safety Limiter, Soft Vacuum, Diopser, Crossover, Buffr Glitch,
Puberty Simulator, Crisp, Loudness War Winner. Own editor opened in the app and closed without trouble: TDR Nova, TDR Kotelnikov,
Airwindows, Surge XT Effects, Dragonfly Hall, Wolf Shaper, Spectral Compressor, Diopser, Crisp. No editor of their own:
MaPitchshift, Safety Limiter, Soft Vacuum, Crossover, Buffr Glitch, Puberty Simulator, Loudness War Winner ("This plug-in has no
editor window."). State: every loaded plug-in's state was saved into `mixer.json`; a changed Airwindows knob (through its own
accessible slider) came back after two restarts; for every plug-in a changed parameter survived save and load into a new
instance (scratch program). Refused with one plain sentence: Surge XT (synth) "This is an instrument, not an effect.", DPF 3BandEQ
"Windows Smart App Control blocked this plug-in." (every time), DPF PingPongPan (blocked in the app, allowed in one scratch run:
Smart App Control's verdict on unsigned plug-ins is not stable). Latencies reported: TDR 184 frames, Spectral Compressor 2048,
Puberty Simulator 1024, Soft Vacuum 5.

**Capture and replay can't run on this PC.** With the test sound (PowerShell playing a quiet looping WAV), FX on started the run,
then: "the recording went silent when the original was muted"; the mode card's Test said "Muting Windows PowerShell 5.1 also
silenced its recording, both ways." So on this PC process loopback is taken after the session's mute and volume: the open
make-or-break question from the engine research is answered "no" here. Without VB-CABLE (not installed; a driver was out of scope)
no program engine can run, so processed sound through a VST was **not heard or metered in the app**; the chain itself was run by
the scratch program instead. The row and FX window said honestly what happened every time.

**Faults found and fixed (shared code).**
- A plug-in path that no longer exists, or a bundle without a Windows binary, threw `FileNotFoundException`, which nothing caught:
  opening its editor or starting the chain would have ended OmniControl (and a saved chain would do it again at every run). Now
  `Core/Engine/Audio/Vst3File.cs` checks the file header first (never loads code) and `Vst3Effect.Load` turns every failure into
  one sentence: missing file, 32-bit, no Windows version, Smart App Control block (`Core/Engine/AppControl.cs`, now also used by
  `HelperProcess`), instrument, no effect, or "Couldn't load the plug-in: ..." for the rest.
- Mono plug-ins were refused ("Only stereo plug-ins can run."). `Vst3Stage` now feeds a mono input the mix of left and right and
  copies a mono output to both sides.
- Plug-in latency was not in the delay the row shows. `IDspStage.LatencyFrames` (plug-ins only), `DspChain.LatencyFrames` (read
  live, plug-ins may change it) and `ProgramRun.ShowRunning` now add it ("about N ms delay").
- The scan listed 32-bit and non-Windows bundles next to the real ones (TDR ships both folders). `PluginScanner` leaves them out;
  a hand-picked file says why it can't be used. A VST2 `.dll` picked by hand now says "VST2 plug-ins (.dll) can't run in
  OmniControl; choose the plug-in's VST3 version (.vst3)." (before: "That isn't a VST3 plug-in"). (Superseded 14 Nov: VST2 plug-ins are hosted now, see "VST2 plug-ins, plug-in folders".) Note for AGENTS.md: VST2 is not
  "stored only", it is not accepted at all; the file picker offers only .vst3.
- **Exit hung for good** after a JUCE plug-in's editor (Airwindows) had been read by a UI Automation client: unloading it got stuck
  inside Windows UI Automation (native stack: Airwindows -> UIAutomationCore -> combase wait on the UI thread; most likely JUCE
  disconnecting every UIA provider of the process while Avalonia's providers wait for the same UI thread). Reproduced twice; not
  reproduced in a WinForms test host. Fix: `EffectsEngine.Dispose` only saves plug-in states; `ShellViewModel.Dispose` unloads the
  plug-ins as its very last step inside `Services/ExitGuard.cs`, which ends the process if that takes longer than
  `Timing.PluginUnloadLimit` (3 s). Checked: the same sequence now exits after about 4 s with settings saved.
- Skipped-effect notes read "(... (Smart App Control).)"; the note now drops the full stop and the sentence has no brackets.
- Tidied: the bare `catch (Exception)` in `PluginScanner.Walk` is now IO/access errors only.

**Not fixed (open items).**
- The same UI Automation hang can happen in the middle of a session when plug-ins are unloaded (`ReleaseUnused`, after the last
  run stops) while a UIA client (screen reader, accessibility tool) is connected; there is no safe guard for that on the UI thread.
  Real fix: out-of-process plug-in hosting (one helper process per plug-in or per chain), which would also contain native crashes.
  Not built (not asked).
- (Done 14 Nov, see "FX editors for small screens": Parameters shows any plug-in's settings as sliders.) Plug-ins without their own editor (7 of 18 here) can't be edited at all: OmniControl has no generic parameter cards. Proposal:
  `Vst3Effect` exposes its parameter list (title, units, steps, normalised value, the plug-in's own value text); `FxSlotViewModel`
  shows one slider card per automatable parameter with the plug-in's text; changes go through NAudio's parameter queue and are
  saved with the state. Needs the owner's go-ahead (new card kind, rule 6 checks).
- Only the first effect class of a .vst3 file is used (multi-plug-in bundles such as some LSP or Surge builds would show one
  entry); a class picker would need loading at scan time.
- NAudio.Vst3 hard-codes `ProcessMode.Offline` for setup and process, and passes no output-parameter list and no process context:
  plug-ins with an "offline quality" switch (TDR) may use their render quality live, DPF plug-ins print assertion lines (harmless),
  tempo-synced effects get no tempo. Fixing it means a patched NAudio.Vst3.
- Side-chain and extra buses stay inactive (Kotelnikov, Spectral Compressor, Crossover run on their main bus only).
- With Wet below 100 % a plug-in with latency is mixed with an undelayed dry signal (comb filtering); the dry path would need the
  same delay.
- Plug-ins loaded for their editor stay loaded until a run stops or OmniControl exits (unloading earlier would widen the hang
  above). Large editors are not limited to the screen: Spectral Compressor's is 1924 px wide here. (Addressed by the FX window rework below:
  the editor's space stays within the screen and scrolls.)
- Seen while testing, not VST related: OmniControl idles at 10 % (Displays page) to 20 % (Volume page, meters on) of one core with
  the PC locked; list items expose their view-model type as their UI Automation name (screen readers would read
  "OmniControl.App.Fx.FxSlotViewModel").

**Not verified.** Nothing was heard (PC locked, sound quiet, no engine could run); no editor was seen (only its window size and
accessibility texts); wet below 100 % and bypass were changed in the FX window and saved, but not run through audio in the app;
the VST2 and 32-bit messages were produced by the code path, not by picking files in the dialog; behaviour with the PC unlocked,
at other DPI settings and with a real screen reader.

**Owner test steps.** 1) Add a VST3 in a program's chain, open Plug-in window, change something, close OmniControl from the window
or tray: it must close within a few seconds; restart: the change is kept. 2) A mono plug-in (DPF MaPitchshift) now loads. 3) Once
VB-CABLE is installed (Virtual sound device), check the row's delay includes a plug-in's latency (Spectral Compressor adds ~43 ms).

## Close behaviour and Start with Windows (13 Nov) - compiled only; nothing run or seen on screen

- **Close.** `AppSettings.CloseBehavior` (Ask default, MinimizeToTray, Exit) replaced the old `CloseToTray` bool (an old settings.json's `CloseToTray` is ignored, so everyone starts at Ask). The X button: Ask opens `Shell/CloseChoiceWindow` (calm one-sentence text, "Remember my choice" switch writes the same setting; Esc or its X does nothing); MinimizeToTray hides the window (hotkeys, mouse hook, OSD and engine keep running); Exit goes to the one exit path (`WindowLifecycle.Exit` -> `desktop.Shutdown()` -> `desktop.Exit` -> `ShellViewModel.Dispose`). The tray icon is now always shown (it used to follow the old switch) with Open OmniControl and Exit; a click on the icon opens the window (Avalonia's `Clicked` is a single click, so double-click opens too). The "When I close the window" card on App settings is the same setting and re-reads it when the page opens. `AppLifecycle.TrayEnabledChanged` was removed.
- **Start with Windows.** `Core/Engine/StartupEntry` does the registry work (HKCU Run + StartupApproved\Run); `App/Services/StartupRegistration` owns the entry name, the `--background` argument and the command line `"<exe>" --background`. A background start builds the window and hides it at once (tray only); everything else runs. The card shows what Windows really has (`StartupState`: Off / On / DisabledByUser); when the user switched it off in Task Manager the card shows Off with a one-line note, and turning it on writes the entry and clears that flag. `RepairIfMoved` rewrites a stored command that differs from the current exe path at start-up (without touching the Task Manager flag); an entry from before this change is rewritten with `--background` the first time, so it then starts hidden. `StartMinimised` is unchanged (the tray is always there now, so it always hides to the tray).
- Unverified: the dialog's look and focus, hiding the window right after it opens at a background start (a brief flash is possible; it starts minimised first), the tray Exit / Open on the owner's PC, and that Task Manager's flag semantics (first byte odd = disabled) match every Windows build.

## FX window rework: one chain panel, embedded plug-in editors, multi-select and drag, owned windows (13 Nov) - compiled only (throwaway build); nothing run, seen or heard

> Superseded in part on 15 Nov ("FX window tidy, keyboard instructions off screen", the last sections): the keyboard footer strip, the CPU number, the Output picker in the header and the controls under and above the editor are gone; the toolbar row, the status strip and Options > Plays on / Keyboard shortcuts replace them.

Brief: embedded plug-in editors with pop out / put back, serial order made explicit, smooth drag of the selection, rubber-band and
Windows selection keys, the FX window buried under the main window, TabForge's FX loader as the model. Mid-job design change from the
owner (Volume page "Design D": a row expands to show its chain): the chain is now ONE reusable control with no window knowledge, and
the FX window is a thin host of it. (Later the owner went back to the FX window only: see "Effects back in their own FX window"; the
Volume row no longer hosts the panel, and `InlineEditor`, `TabSwitchesPane`, the Edit pill and the heading line are gone.) TabForge (MIT, same owner, commit 9bb7236, cloned read-only into the scratch folder) is Avalonia
too, so code was ported closely where it fits; credited in `NOTICE.txt`.

**What was built.**
- `Fx/FxChainPanel` + `FxChainViewModel` (+ `.Edits.cs`): the chain control. Left: a calm heading "The sound passes these effects in
  order, top to bottom.", numbered rows (number, power switch, name with its kind as tooltip, an `Edit` pill), Add / Remove. Right: the
  selected effect's cards (equaliser, sliders) or its plug-in editor, then the toolbar (preset, Wet, On, Pop out / Put back, runs note).
  The left list is now a fixed width (`FxChainListWidth` in `App.axaml`) instead of 30 % of the window, so the editor space grows
  one for one with the window (the window-fit maths needs that). (Since restored: the "VST3/FX" column is back, the Edit pill and the
  heading are gone, the width is 246, the old share at the window's first size.)
- `Fx/FxWindow` + `FxWindowViewModel`: menu (now also Copy, Paste, Pop out, Put back, Forget program), output device, engine line,
  the panel, shortcut bar. "Forget program" moved from the list's button row to the FX menu. The window opens centred on its owner.
- **Serial order**, confirmed in code: `ChainBuilder.Build` walks `MixerService.FxChain(key)` in list order and `DspChain.Process` runs
  the stages in that array order; `MasterChainWriter` writes the APO lines in the same order. Every reorder (drag, Alt+Up/Down, paste)
  changes the list and the stored chain together (`FxChainViewModel.Reorder`); `DspChain.TakeStateFrom` matches state by slot and
  plug-in instances are keyed by slot in `Vst3ChainHost`, so moving never hands state to another effect.
- **Drag** (`ListDragReorder`, extended, still the one shared piece): a press on a row of a multi-selection keeps the selection and drags
  all of it as one block (a plain click without moving then selects that row alone); other rows slide aside by render transform; one
  list change on drop (`MoveRows(rows, insertAt)`); Esc cancels. Rows must be one height (they are).
- **Selection:** the ListBox's own Multiple mode already gave Ctrl+click, Shift+click, Shift+arrows, Ctrl+A; `RubberBandSelect` already
  drew the rectangle from the empty part of the list (now with the shared start distance). Added: Esc reduces a multi-selection to one
  (a second Esc closes the window); a row's power switch or the toolbar's On switches the whole selection when the row is part of it;
  Delete, Space, Alt+Up/Down and drag act on all of it; Ctrl+C / Ctrl+V copy and paste effects (`FxClipboard`, inside OmniControl,
  shared by all chains; a plug-in is copied with its current settings).
- **Embedded editors:** selecting a plug-in shows its own editor inside the panel (only the selected effect's editor is open: the chain
  is a list, so one editor at a time). `PluginEditorArea` grows the FX window to the editor (never shrinks it; never past the working
  area less 40 DIPs; display scale taken into account; a scale change is passed to the plug-in). An editor still larger (e.g. 1924 px
  wide) is **scrollable**, not scaled: two scroll bars beside it move the plug-in's own window inside its host (`Vst3Editor.MoveView`);
  a host can't scale a plug-in's native window. Resizable plug-ins (canResize) are offered the whole space (checkSizeConstraint, then
  onSize) when it changes; a plug-in's own resize request (IPlugFrame::resizeView) grows the window again. (The inline mode for a
  Volume row, `InlineEditor` / `EditorSizing.GrowArea`, was removed with the in-row expansion.)
- **Pop out / Put back:** (since 14 Nov for every effect, `EffectEditorWindow`, see "FX editors for small screens") `Pop out` (toolbar, FX menu) moves the editor into its own window (`PluginEditorWindow`, sized to the editor
  within the screen, resizable only when the plug-in is); the panel then says "This plug-in's window is open on its own. Put back shows
  it here." with a `Put back` pill. Closing the popped-out window also puts it back. Remembered while the chain's panel is open
  (several can be popped out); popped-out windows close with it.
- **Contained failures:** load errors, "no editor", a plug-in that throws while making its view (now caught in
  `Vst3ChainHost.OpenEditor`), a refused attach and Windows refusing the child window are each one sentence in the editor space.
  Editors are detached while their host window still exists: the FX window disposes its chain on `Closing`, a popped-out window
  disposes its editor on `Closing`, and `PluginEditorHost` detaches before its native window goes. `Vst3Editor` is safe to call after
  dispose. Exit order unchanged: `FxWindowService.CloseAll` before the plug-ins are unloaded last under `ExitGuard`.
- **Z-order bug:** cause confirmed: `FxWindowService` showed FX windows and plug-in editor windows with `Show()`, unowned, so they were
  equal top-level windows and the main window could cover them. Fixed once in `Services/OwnedWindow.Show(window, owner)`: owned by the
  main window (or the given one; popped-out editors are owned by the FX window, or by the main window when the panel is in a page).
  Also used now by the About window and the effects-setup message window. Dialogs (Add effect, action picker, mouse layer manager,
  close choice) already used `ShowDialog(owner)`. The OSD and gesture margin overlays stay topmost by design. "Keep on top of other
  programs" (Options menu) still makes FX windows topmost. Side effects (standard Windows behaviour): FX windows minimise and return with
  the main window; an FX window opened while the main window is in the tray is unowned.

**For the Volume page job (Design D).** Superseded: the Volume row no longer hosts the chain; `FxWindowService.CreateChain` is private and
the only way in is `FxWindowService.Open(key)`.

**Not verified.** Only a clean compile into a throwaway folder; the app was not run; no window, editor or drag was seen; nothing was
heard. In particular: the plug-in window moved with SetWindowPos inside Avalonia's child window while scrolled; plug-ins that answer
onSize with their own resize request (expected to settle); the window-grow arithmetic with the wrapping toolbar (its height can change
with width); per-monitor DPI moves; press on a selected row then release (select alone); Avalonia's own Ctrl+A / Shift+arrows here.

**Open items.**
- Selecting a plug-in now loads it on the UI thread to show its editor (before, only "Plug-in window" did): a slow plug-in makes the
  selection slow; a Smart App Control block shows its sentence in the editor space.
- No edge scrolling while dragging or drawing the rectangle in a long list (TabForge has it); Esc does not cancel a rectangle.
- The FX window grows for a large editor but never shrinks back.
- `mixer.json` is saved whenever an editor closes (a selection change away from a plug-in), through `Vst3ChainHost.EditorClosed`.
- Literal font sizes in `FxChainPanel.axaml` / `FxWindow.axaml` kept (existing open item "Font sizes and spacing").

**Owner test steps.**
1. Open a program's FX window over the main window; drag it across the main window and click the main window: the FX window stays
   above. Pop out a plug-in and do the same.
2. Add a VST3 with an editor (TDR Nova): its editor shows inside the window, which grows to fit. Select an Equaliser: cards show; back
   to the plug-in: its editor again, settings kept.
3. A resizable plug-in (Surge XT Effects, Airwindows Consolidated): resize the FX window, the editor follows; its own resize, if any,
   grows the window.
4. Spectral Compressor (1924 px wide): the window grows to the screen width less a margin; a horizontal scroll bar moves the editor.
5. Pop out, move and close the pop-out window; Put back (toolbar, notice pill, menu). Pop out two plug-ins at once.
6. Drag one row; Ctrl+click three rows and drag one of them: all three move together, the others slide aside; Esc during a drag cancels.
7. Draw a rectangle from below the last row up over rows; Ctrl+click, Shift+click, Ctrl+A, Shift+Up/Down; Esc reduces to one, Esc again
   closes. With several selected: Space, a row's switch, Delete; Ctrl+C then Ctrl+V (also into another program's FX window).
8. The numbers 1, 2, 3 follow every move; with an engine running, reorder while music plays: no clicks.
9. Both themes; the narrowest FX window (560 px): the right pane is about 280 px (list 246).

## Volume page "Design D", sound modes and routing (13 Nov) - plan, written before coding (as-built notes in the next section)

**Decisions (from the repo and the brief; none needed the owner).**
- **Two sound modes, no global Off.** Every row keeps its own effects power switch, and Fast with every switch off leaves Windows
  alone (no APO line, no capture, no routing), so a third "Off" would only repeat the switches. A row whose stored mode was "off"
  gets its effects power switched off by the migration, so nothing starts by itself after the update.
- **Fast** = the old Hybrid: the master's chain goes to the user's Equalizer APO (unchanged path); a program's chain runs through
  Capture and replay (`ProgramEngine`); no cable is used. **Full control** = everything OmniControl processes goes through a
  VB-CABLE and OmniControl's own engine: the master bus, combined devices, processed microphones, programs with effects or several
  outputs. Each of those needs its own cable.
- **Full control does not change Windows' default playback device.** The master bus sends every program that follows the master to
  its cable with the per-program routing OmniControl already uses and journals (`MixerService.HoldRouting`, `EngineJournal`), so no
  new undocumented interface sits in the engine's crash path. Limits: Windows' own system sounds, and a program during its first
  scan interval (`Timing.MixerRefresh`), play directly on the default device.
- **Master "plays on" one device = Windows' default playback device.** Ticking another device in the master's list makes it the
  default (a user action like Windows Settings; `CoreAudio.SetDefaultDevice`, the undocumented IPolicyConfig that EarTrumpet and
  SoundSwitch use; not journaled because it is the user's own choice). Several devices (Full control only): the master bus plays on
  all of them and the default stays the first. The master volume slider stays the default device's volume, so with several outputs
  it moves the first device only (said in the tooltip; open item).
- **A combined device is a cable.** Combined speakers own one cable: whatever is sent to it (the master, a program, or another app
  that picks it in its own settings) is captured and played on every member. A combined microphone owns one cable too: its members
  are recorded, mixed, run through its chain and played into the cable; apps record from the cable's recording side ("CABLE-A
  Output"). The cable of a combined device or a processed microphone is remembered (apps keep pointing at the same "CABLE-x
  Output"); the master bus and program runs take any free cable.
- **Clock drift between devices:** the first output is the clock; every extra output reads its own queue through NAudio's
  `WdlResampler` with a rate nudged from that queue's level (at most +-0.5 %); a queue that still runs too long is trimmed.
- **Equalizer APO and microphones (checked in the upstream source):** Equalizer APO can be installed on recording devices
  (`DeviceAPOInfo.cpp` reads the Capture key; `Stage: capture` exists). OmniControl does not write recording chains to it yet, so
  in Fast mode microphone effects stay greyed (open item). Found while checking: OmniControl's include sits at the top of
  config.txt with no device line, so the master chain also ran on every other device (and microphone) that has Equalizer APO; the
  master's `OmniControl.txt` now starts with `Device: {guid}` of the default playback device.
- **Housekeeping:** setup tools (Equalizer APO's Device Selector, Configuration Editor, installer and uninstaller, VB-CABLE's setup)
  are never added as program rows (one list, `App/Audio/IgnoredPrograms.cs`); remembered entries of them are removed at start-up
  through `MixerService.Forget`, the same path as the new remove button on a remembered program's row.

**Logic matrix ("greyed" = visible, dimmed, the reason as tooltip and a small "Switch" link).**

| Control | Fast | Full control | What the engine does |
|---|---|---|---|
| Master effects | yes: Equalizer APO on the default device (the status says when it is missing or not on this device) | yes: master bus (cable -> chain -> outputs), needs a free cable | APO path as before; master bus new |
| Master plays on 1 device | yes (sets Windows' default) | yes | new, not run |
| Master plays on several / a combined device | greyed "Needs Full control mode" (the APO processes one device) | yes: the bus fans out; a single combined device becomes the default (its cable) | new, not run |
| Program effects | yes via Capture and replay; honest status (on this PC the capture goes silent when the program is muted; the stored Test result stops it before anything is touched) | yes: program -> own cable -> chain -> outputs; "Needs another virtual cable" when none is free | existed with one output; several outputs new |
| Program plays on 1 device | yes (per-program routing, as before) | yes (also onto a combined device's cable) | existing routing |
| Program plays on several | only where Capture and replay can run (Windows 2004+ and the last Test did not fail), else greyed | yes (own cable) | new fan-out |
| Program plays on a combined device | greyed | yes | new |
| Combine speakers / microphones | greyed | yes (one cable each) | new runs |
| Microphone effects | yes via Equalizer APO on that microphone (14 Nov; greyed only when Equalizer APO is not installed; "not on this microphone" is the row's status with Choose devices) | yes: mic -> chain -> own cable (apps pick the cable as microphone) | Fast path new 14 Nov (see "Microphone effects in Fast mode") |
| Combined microphone effects | greyed: Needs Full control (its members are mixed through a virtual cable; Equalizer APO only processes one device's own sound) | yes (own cable) | unchanged |
| Pan | master and programs, as today | same | unchanged |

**Data model (mixer.json; old files keep loading).** `MixerSettings.SoundMode` ("fast" / "full"); `MasterPlaysOn` (targets, used for
several outputs); `AppAudioEntry.PlaysOn` (targets; empty = follows the master); a target is an endpoint id or `combo:<id>`;
`DeviceNames` (id -> last known name, so an unplugged device keeps its name); `Combined` (`CombinedDevice`: id, name, kind, members,
remembered cable; for microphones also effects power, chain, volume, mute); `InputFxEntry.Cable`. Migration (`MixerMigration`, run by
`MixerSettings.Load`): global mode = Full control when a program had "virtual" with its effects on, else Fast; a row whose stored
mode was "off" (or empty, which meant Off for programs) gets its effects power off; "capture", "virtual", "apo" keep their switch;
`DeviceId`/`DeviceName` become `PlaysOn` + `DeviceNames`. Old fields are read once and written back empty.

**Master lists.** `App/Audio/SoundModes.cs` (the modes: id, name, description, details, tags, setup kind, plain status from what the
PC has, and the matrix above as `SoundModes.Check(feature, mode, facts)`). Hotkey "Switch sound mode" keeps the id
`volume.fxmode.next` so saved bindings work. Search lists Sound mode (gallery card), Combine devices (button card) and every row.

**File plan.** Core: `Engine/Audio/VirtualCables.cs` (discovery and pairing of a cable's playback and recording sides; the one name
list moves here), `Engine/Audio/PipeOutputs.cs` (extra outputs with drift correction), recording devices as `PipeSource`, several
outputs in `AudioPipe`, `CoreAudio.DefaultDevice.cs`. App/Audio: `SoundModes.cs`, `SoundModeService.cs` (replace `FxMode.cs`,
`FxModeService.cs`), `CombinedDevice.cs`, `MixerMigration.cs`, `IgnoredPrograms.cs`, `MixerService.Routing.cs`. App/Audio/Engine:
`RoutePlanner.cs` (pure: what runs where, which cable), runs `BusRun.cs`, `FanOutRun.cs`, `MicRun.cs`, `ProgramEngine` and
`CableRoute` with several outputs, `EffectsEngine` reworked. Pages: `KeyScope.cs`, `OptionPanelKeys.cs` (moved out of the old mode
button), `Pages/Volume/VolumeRow.cs` (shared row model; replaces `IVolumeRow` and the copied FX wiring), `ModeGate.cs`, `RouteTick.cs`,
`SoundModeChoice.cs`, `RowSentence`, `RowEffects`, `SoundModePanel`, `CombineWindow` + `CombineDeviceViewModel`. Removed:
`FxModeButton`, `FxModeChoice`, `FxSplitButton`, `IVolumeRow`, `VolumePageViewModel.FxModes.cs`, `Button.pill.mode` and the FX split
styles. `Styles/Theme.axaml` is over 700 lines: its brushes move to `Styles/Brushes.axaml`.

## Volume page "Design D", sound modes and routing (13 Nov) - as built; compiled only (throwaway builds, 0 warnings, 0 errors); nothing run, seen or heard

Built to the plan above (decisions, matrix, data model). File map in `ARCHITECTURE.md` ("Sound modes", "Mixer state", "Effects
engine", "Volume page", "Option list", "Keyboard model", "Styles"). Differences from the plan and things to know:

**Matrix as built** (`SoundModes.Check`; a gated control is dimmed, its reason is the tooltip, Switch changes the mode):
- Fast greys: the master's second device (the "plays on" list becomes one choice, its footer says why with Switch), combined
  devices in any list, "+ Combine devices" and the combine search card, combined-device rows, microphone effects. A program's second
  device is greyed only when recording a program can't work here (Windows older than 2004, or the stored Test result "failed"); an
  untested PC counts as working (the engine checks while it runs, as before).
- Full control greys nothing; what is missing is the row's status: "Needs VB-CABLE" (none installed), "Needs another virtual cable"
  (all in use), "None of its devices is plugged in", "Starts when the program plays".
- Master and program effects are never greyed: the row status says what runs ("Fast mode · Using your Equalizer APO · no extra delay",
  "Effects running · about 45 ms delay", "Equalizer APO is not on this device. Playing without effects.", "Recording a program stops
  when it is muted on this PC. Playing without effects.").

**Engine (`RoutePlanner` + `EffectsEngine`).** One plan per change (settings, scan, mode, what the PC has). Fast: master chain ->
Equalizer APO (as before, now `Device: {guid}` limited); a program with effects or several devices -> `ProgramEngine` on all of them
(none of a program's devices are combined devices in Fast). Full control: cables are given out in this order: combined devices and
processed microphones that remember a cable; the master bus when the master has effects or several devices; the other combined
devices and microphones; then playing programs that need one. `BusRun` routes every program that follows the master (no device of
its own, no run of its own) to its cable, each journaled; `CableRoute` sends one program to its own cable and plays on its devices
(a program that follows the master plays into the master bus); `FanOutRun` plays a combined speakers' cable on its members;
`MicRun` records microphones into a cable. A single device for a program is per-program routing (also a combined device's cable);
for the master it is Windows' default device (`CoreAudio.TrySetDefaultDevice`), so a master on one combined device makes that
device's cable the default. A run whose spec is unchanged keeps running; any change of outputs restarts it (a short gap).
Several outputs: `AudioPipe` plays on all; extra devices follow the first through `ExtraOutput` (WdlResampler, +-0.5 % nudge, target
40 ms queue). The delay shown is the slowest device's.

**Migration and stored data.** `MixerMigration` runs once per old file (no `SoundMode`): see the plan. Combined devices, "plays on"
lists, device names and remembered cables are new fields; old files load. The owner's leftover "Equalizer APO Device Selector" entry
is removed at the first start by `IgnoredPrograms` through the same remove path as the row's × (mixer.json is rewritten then).

**Page.** One row view for every kind of row (`VolumeRowView`); the old `MixerRow`, `InputRow`, `RowIdentity`, `FxSplitButton`,
`IVolumeRow`, `FxModeButton`, `FxModeChoice`, `VolumePageViewModel.FxModes.cs`, `FxMode.cs`, `FxModeService.cs` and the `pill.mode`,
`fxsplit`, `fxflat`, `fxpower` styles are gone. The FX split button's power moved into the expanded part (a switch "Effects on/off");
since the move back to the FX window it is the "Effects on/off" switch in the FX window's header; the sentence shows "2 effects, off"
when the chain is switched off. The Inputs group is called Microphones (same stored key, so its
open/closed state is kept); both groups now start open. Rows keep their open chain across scans and page visits. A program's Test
(Fast mode, open programs, with effects) is a link on the row's sentence line (it was in the expanded line, removed). Microphones no longer list cables' recording sides.
- Keyboard: Left/Right walk a row's parts (route link, effects link, Switch / Test links, pan, level, mute, FX, ×); Enter (or one click)
  on the effects link or FX opens the row's FX window (it used to expand the row; `RowEffects` and `KeyScope` were removed). (13 Nov:
  the "plays on" pop-down and the header line are gone; the route link now opens the Wiring tab, where the Sound mode and Combine
  buttons are Up/Down stops.) The Sound mode panel: Up/Down cards, Right/Left to a card's buttons, Enter picks, Esc closes
  (`OptionPanelKeys`, moved out of the old mode button).
- Search: "Sound mode" (gallery card: Left/Right switches, preview = the mode's first status line), "Combine devices" (button card,
  greyed in Fast), and every row; in search results a row hides only its × (the chain is never shown in a row any more).
- Hotkeys: "Switch sound mode" keeps the id `volume.fxmode.next`; the footer names the mode and its first caution.

**Shared pieces made for this job.** `Controls/OptionList` + `OptionListModel` (13 Nov: cut down to the ticked list the Combine
devices window uses, see "Volume page: Mixer and Wiring tabs"; API `new OptionListModel(Action<OptionEntry> chosen)`, `SetItems`,
`Highlight`, `Move`, `Choose`, `OptionEntry(id, label)` with `IsChecked`; view event `CloseRequested` (Esc)). Also `Pages/KeyScope` (removed with the in-row expansion),
`Pages/OptionPanelKeys`, `OwnedWindow.ShowDialogAsync`, `Styles/Brushes.axaml` (Theme.axaml was over 700 lines).

**Not verified (nothing was run).** Everything on screen: row layout at 720 px (the sentence wraps to a third line when it does not
fit; links trim at `SentenceLinkMaxWidth`), both themes, the link look (underline set on the link's text), the pop-downs, the
combine window, focus moves. Engine: no default
device was changed, no cable routed, nothing played: the IPolicyConfig call, several outputs and the drift correction, the master
bus routing every program, fan-out and microphone runs, combined devices with real cables, whether Windows moves a running program's
stream when its per-program device changes (the existing routing comment says only new streams). Compile-checked only.

**Open items.**
- Master volume with several devices moves only Windows' default device (tooltip says so); a software master level for the bus would
  need the slider, OSD and hotkeys to change together.
- Full control: Windows' system sounds and a program in its first 2 s play directly on the default device (no default-device switch,
  by decision); a program that ignores per-program routing bypasses the bus.
- (Done 14 Nov: Fast-mode microphone effects through Equalizer APO, one `Device:` section per microphone; see "Microphone effects in
  Fast mode".)
- Combined rows have no meter; a combined microphone's meter could read its cable's recording side.
- A run restarts on every change of its outputs (no seamless re-route); the bus restarts when the master's devices change.
- `BrushOn`, `BrushOnHover`, `BrushCaution`, `BrushDanger`, `BrushCautionSoft` lost their last users (FX split button, mode pill);
  kept as palette entries; the effects switch is now Fluent's accent switch, not the green power button (owner to decide).
- A combined device's members can't include cables (on purpose: no loops); removing a combined device asks first.
- `VolumePageViewModel` still calls Core Audio directly for the master and microphones (older open item, unchanged).

**Owner test steps.**
1. Build and launch. Volume page: header "Sound mode: Fast" (or Full control if a program had used Virtual sound device with effects
   on); every row reads "plays on ... · N effects". The old "Equalizer APO Device Selector" row is gone.
2. Fast, Equalizer APO on: master row > its effects link (opens the FX window) > add an Equaliser with a strong band > Effects on. Line: "Fast mode ·
   Using your Equalizer APO · no extra delay"; `OmniControl.txt` starts with `Device: {...}`. Effects off: the line disappears.
3. Fast, Equalizer APO off (or another default device): the master's line says why ("Equalizer APO is not on this device") and the
   Sound mode panel's Fast card offers Choose devices / Install Equalizer APO.
4. Master "plays on": pick another device: Windows' default device changes (check Settings > Sound). In Fast a second device can't be
   ticked (round marks, footer "Needs Full control mode" + Switch).
5. A program: "plays on" > Headphones: it moves there (as the old Output picker). Its effects in Fast: Test, then Effects on (on this
   PC the status should say recording stops when muted, and the program keeps playing normally).
6. Without VB-CABLE, switch to Full control: the card says "Needs VB-CABLE" with Install VB-CABLE; rows with effects say "Needs
   VB-CABLE"; nothing is routed (check Windows' volume mixer: every program on its device).
7. With VB-CABLE (spare PC preferred): Full control, master effects on: programs move to "CABLE Input" in Windows' volume mixer and
   the sound plays processed on the speakers; Effects off or Fast: every program comes back. Close OmniControl while it runs: all come
   back. Tick two devices for the master: both play (listen for drift over 10 minutes).
8. "+ Combine devices" (Full): name, Speakers, tick two devices, the status names a free cable or says one is needed, Create. The new
   row appears; a program's "plays on" offers it.
9. Both themes, the narrowest window (720 px), search for "sound mode", "combine", a program's name.

## Custom gesture rules and the master picker (13 Nov) - plan, written before coding (as-built notes in the next section)

**Decisions (from the repo and the brief).**
- **Storage:** a new `rules.json` owned by `App/Rules/RuleSettings` (one class per json file). `gestures.json` keeps the fixed
  triggers keyed by trigger id; rules are a list with their own shape and the command approvals, so they get their own file.
- **One stored-text format for every block.** A trigger, a condition and an action step are each stored as the same
  `"id:parameter|key=value;..."` text that mouse buttons and gestures already use (`MouseAction.Parse` / `Compose`,
  `MouseChoice.Matches` / `WithParameter`). So every slot is picked with the same picker entries and settings pages.
- **Rule** = id, name, on, trigger (one), conditions (any number), `AnyOf` (false: all must hold ("and"), true: any ("or"); one level
  only, no nested groups), steps (any number, run in order), layer (0 = any), rate limit in ms (default `Timing.RuleRateLimit`, 1 s).
- **Layer** is the editor's drop-down (Any or one mouse layer, names from `MouseSettings.LayerLabels`). It is not also offered as a
  condition: one place for one idea.
- **Triggers master list** (`App/Rules/RuleTriggers.cs`, one entry per trigger: id, name, the phrase it reads as, its kind and button):
  right, middle, back, forward click; double click (left); scroll up/down; tilt left/right; press and hold right, middle, back,
  forward (hold time option); key or shortcut (parameter "Ctrl+Alt+K"). The engine dispatches on the entry's kind, so a later job adds
  trackpad triggers as new entries plus their input source, without touching the builder. A plain left click is not offered (it is how
  everything is clicked; a rule on it could lock the user out).
- **Conditions master list** (`App/Rules/RuleConditions.cs`): over a screen area (every `PointerAreaKind`, named by
  `GestureCatalog.AreaName`), in a program (the programs with a window now plus the Volume page's remembered programs, plus "Another
  program" with a typed name; the window under the pointer for mouse triggers, the active window for a shortcut), keys held (every
  combination of Ctrl, Alt, Shift, Win; held keys count exactly, as for gestures). As for gestures, a rule without a "keys held"
  condition does not fire while a modifier key is held (so Ctrl+right click keeps its meaning).
- **Steps** (`App/Rules/RuleSteps.cs`) = the mouse button action list (`MouseActionCatalog.Choices` over the live `HotkeyRegistry`:
  OmniControl actions, Windows functions, media keys, send a shortcut, open a program/file/link, open a folder) minus what needs a
  held button (modes, layers, Default, Disabled), with the press/release/repeat and "also send" settings left out, plus two steps only
  a sequence has: "Run a PowerShell or CMD command" (command, shell, hidden window (on), timeout) and "Wait" (ms). Windows functions
  (lock, screenshot, show desktop, task view, media keys) are already entries of the built-in action table, so nothing is added twice.
  They are not added to Shortcuts: a hotkey that sends another Windows shortcut would send it while the hotkey's own keys are still
  held (Ctrl+Alt+L would arrive as Ctrl+Alt+Win+L), and Windows has its own keys for them (open item for the owner).
- **Precedence:** a new `MouseHook.AddHandler(handler, first: true)` puts the rule handler ahead of the wheel gestures and the mouse
  service (which asks the middle-click gestures before the button mapping). So: rules, then built-in gestures, then Mouse page mappings.
  The first matching rule in the list wins. A rule takes the input (the press and its release, or the scroll) only when it matches.
- **Conflicts:** a rule whose trigger and area match a built-in gesture that is on, or whose button has a Mouse page mapping on its
  layer, shows a short chip on its line ("Overrides a gesture" / "Overrides a mouse mapping", detail in the tooltip).
- **Own window:** a rule never runs while OmniControl's window is the active window or under the pointer (the input then passes on).
- **Rules obey the page's "Enable gestures" switch** (and its hotkey), so one switch stops every gesture.
- **Shortcut triggers** are registered with Windows like Shortcuts' hotkeys (own `HotkeyService`), so the shortcut is taken
  everywhere while the rule is on; one already used by Shortcuts or another program is refused with a message.

**Engine (no allocation in the hook's hot path).** `RuleService.Apply` compiles the on rules into an immutable snapshot: one array of
compiled rules per `MouseButton` (empty arrays when unused), each with its areas as a flag set, held-key sets, program names
(lower-case), layer, kind, hold time, rate limit and its parsed steps. The hook handler: one array lookup for the button (an input no
rule uses costs that and nothing else); then own-window check; then per rule: layer, keys held, area (asked once per input, only if a
rule needs it), program (pid -> name cache, only if needed), rate limit and "already running". Double click: the last left press time
and place are kept (two longs); the second press within Windows' double-click time and size is taken. Press and hold: the press is
taken and a timer started; a release before the hold time gives the click back (sent from a worker, never inside the hook), after it
the rule runs. Steps run on a worker (`RuleRunner`): waits, commands (`ScriptRunner`), everything else through a
`MouseActionRunner` (its launch limiter included).

**Safety (AGENTS.md principle 10).** The whole rule is rate limited (and never runs twice at once). Keys are only ever sent as a
complete press-and-release (`InputSender.Chord`); no step holds a key. Commands and programs run only from the trigger or Test,
never on load or import. The first time a "Run a command" or "Open a program" step is saved or changed, the exact command is shown
once in the existing message window (`MessageWindow`, "Allow"); the approval is stored as a SHA-256 of the step's stored text in
`rules.json`, so any edit asks again, and the runner refuses an unapproved step at run time too (a hand-edited file runs nothing new).
Commands run hidden by default, never elevated, with a timeout (process tree killed); output is discarded; a failure or timeout goes
to `rules-log.txt` and the rule line shows one plain chip with the detail and "Copy details" in its editor.

**Master picker.** The Mouse page's two-pane picker (`MouseActionWindow` / `MouseActionMenu` / `MouseActionMenuModel`, its
`OptionItem` settings page and `IActionCard`) moves to a neutral `App/Picker/` folder as `PickerWindow`, `PickerMenu`, `PickerModel`,
`OptionItem`, `IPickerCard`, `PickResult` (this also ends the Pages/Gestures -> Pages/Mouse imports). `MouseChoice` becomes
`PickChoice` (same fields plus `Unavailable`: the entry is greyed with its reason and cannot be picked). New in the model: a noun
("action", "trigger", "condition") for its search watermark and "Use this ..." button, and `AskParameter`: picking an entry that takes
text opens its settings page first (the rule editor; the Mouse and Gestures cards keep today's behaviour). Mouse buttons, wheel
gestures and middle-click gestures use it exactly as before. The Shortcuts page has no action picker (it lists every action with its
hotkey box and device chooser), so nothing moves there. `Controls/OptionList` is not used inside the picker: the picker is two panes
with a cogwheel column and a settings page, which OptionList does not have; bending either would change the Volume list. Multiple
choice is not built into the picker: no picker consumer needs it, and OptionList already covers ticked lists.

**UI.** Gestures page: a last group "Custom rules" ("N rules", an "Add rule" pill on its header line as a header tool). Each rule is
one line: switch, "name: If right click over the taskbar and Ctrl + Shift is held, then ...", chips (conflict, needs a trigger,
failed), Edit pill. The editor (`RuleEditorWindow`, a dialog): name, the sentence of block chips (each opens the picker on its slot,
× removes it, a dashed + adds a condition or a step, the "and"/"or" chip flips the rule between all-of and any-of), Layer drop-down
and Rate limit slider (shared card types, so wheel, typing and double-click reset come with them), Test, Delete rule, Close. Changes
apply at once. A new rule with no trigger and no steps is dropped when its editor closes.

**Files.** Core: `MouseHook.AddHandler(first)`, `WindowQuery.ActiveProcessId`, `SystemCommands.DoubleClickTime/Size`.
App/Rules: `Rule.cs`, `RuleSettings.cs`, `RuleTriggers.cs`, `RuleConditions.cs`, `RuleSteps.cs`, `RuleSentence.cs`, `RuleEngine.cs`
(compiled snapshot and hook evaluation), `RuleService.cs`, `RuleRunner.cs`, `ScriptRunner.cs`, `RuleConflicts.cs`,
`CommandApproval.cs`, `ProgramNames.cs`. App/Picker: the moved picker. Pages/Gestures: `RulesGroup.cs`, `RuleItem.cs`,
`RuleRow.axaml(.cs)`, `RuleEditorViewModel.cs`, `RuleChips.cs`, `RuleEditorWindow.axaml(.cs)`. Styles: `Styles/Blocks.axaml`
(the sentence's block chips; Theme.axaml is at 694 lines). Timing: `RuleRateLimit`, `RuleHold`, `RuleScriptTimeout`.

## Custom gesture rules and the master picker (13 Nov) - as built; compiled only (throwaway builds, 0 warnings, 0 errors); nothing run, seen or clicked

Built to the plan above. File map in `ARCHITECTURE.md` ("Master lists": rule triggers, conditions, steps; "Master picker"; "Custom
gesture rules"). Differences from the plan and things to know:

- **Rate limit is a drop-down, not a slider:** 0.1, 0.25, 0.5, 1 (default), 2, 5, 10 s (`Timing.RuleRateLimits`, default
  `Timing.RuleRateLimit`). A slider over 100-10000 ms would step 5 ms per key press. A rate-limited or still-running rule still takes
  the input it matched (it does not suddenly fall through to the normal click), it just does not run again.
- **Timings:** `Timing.RuleHold` (600 ms, the hold trigger's default), `Timing.RuleCommandTimeout` (30 s), `Timing.RuleWait` (500 ms).
- **Picker:** `PickChoice` gained `Unavailable` (greyed, reason under the name, can't be picked) and `AskFirst` (a command or a wait
  is not complete without its settings); `PickerModel.AskSettingsFirst` opens the settings page for such entries and for entries that
  take text. The rule editor sets it; the Mouse and Gestures cards don't, so they behave as before. Small changes to the old picker:
  "Use this ..." now first takes a text setting that is still being typed (before, a click on the button could lose it), and picking
  the current action again returns its text instead of nothing (same result on the cards). Greyed entries in use: a condition already
  in the rule, and "keys held" under a shortcut trigger. The built-in action "Run a program" is now called "Open a program, file or
  link" (same id `run:`, so saved buttons keep working; it always opened files and links too).
- **A command step stores its text as a setting** (percent-encoded), not after the colon: the stored-text format splits at "|", which
  PowerShell commands often contain.
- **Shared runner wording:** `MouseActionRunner`'s two messages now say "button, gesture or rule" instead of "mouse button".
- **Gestures page hint:** the sentence "OmniControl's own window is never affected" (inaccurate for the built-in gestures, see the
  older note above) was replaced by a sentence about custom rules, which do skip OmniControl's window.
- **Not built:** multiple choice in the picker (no consumer); a "mouse layer is" condition (the rule's Layer drop-down is the one place);
  Windows functions on the Shortcuts page (see the plan's reason); nested and/or groups.
- **Known limits:** a shortcut trigger is taken by Windows everywhere while its rule is on, even where the rule's conditions do not hold
  (RegisterHotKey); one already used by Shortcuts or another program is refused with a status message (repeated on every rule edit).
  The first click of a double click passes through to the window under it. A press-and-hold button's short click is given back on
  release, so dragging with that button is not possible over the rule's area. The first time a program condition meets a program, its
  name is looked up inside the hook (opening the process, about a millisecond); later lookups are cached. The program list in the
  condition picker is read on the UI thread when the picker opens. Test runs the steps where the pointer is, which is over the editor,
  so a "window under the pointer" action acts on the editor. A rule does not run while OmniControl is the active window, so the first
  click after using OmniControl, if it lands on the taskbar, is not a rule trigger.
- **Confirm windows** (allow a command, delete a rule) are the existing `MessageWindow` shown through `FxWindowService.AskAsync`; they
  are owned by the main window while the rule editor (a dialog) is open. Unverified that they always come up above the editor.

**Verified by reading code (not run):** every new list is one registration in one place and read live: triggers
(`RuleTriggers.All`), conditions (`RuleConditions`, areas from `PointerAreaKind` + `GestureCatalog.AreaName`), steps
(`RuleSteps.Choices` = `MouseActionCatalog.Choices(HotkeyRegistry.Actions)` each time the picker opens, so an action registered
later appears; plus `Command` and `Wait`, which only make sense inside a sequence). No new OmniControl action was added (rules follow
the existing "Toggle gestures", whose keywords now include "custom rules"); each rule is a search entry on the Gestures page. Only one
picker implementation remains (`App/Picker`); the old `Pages/Mouse/MouseActionWindow`, `MouseActionMenu`, `MouseActionMenuModel`,
`IActionCard`, `OptionItem` and the `MouseChoice` record are deleted.

**Not verified (nothing was run):** everything on screen (the group, rule lines and tags at 720 px and in both themes, the editor's
blocks and dashed "+", the greyed picker rows, focus after each picker and confirm window), every trigger on real input, the hook's
order with rules first, shortcut registration, commands (PowerShell/CMD start, hidden window, timeout and tree kill, exit codes),
the approval flow, the conflict tags.

**Open items.**
- Windows functions as Shortcuts hotkeys (needs a decision: a hotkey that sends Win+L arrives with the hotkey's own keys held).
- Rule order can't be changed in the list yet (first match wins, so order matters); a drag or Alt+Up/Down would fit `ListDragReorder`.
- Trackpad triggers: done 13 Nov (see "Mouse and Trackpad page: trackpad gestures"); no new `TriggerKind` was needed.
- The Mouse page card does not flash for a press a rule took.

**Owner test steps.**
1. Build and launch. Gestures page: the last group "Custom rules" says "0 rules"; "Add rule" sits on its header line (Up/Down stops
   on it).
2. Add rule: the editor opens on "If [+ trigger] [+ condition], then [+ action]". Click "+ trigger" > Mouse buttons > Right click.
   Click "+ condition" > Over a screen area > Taskbar. "+ condition" again > Keys held > "Ctrl held". Click "+ action" > Launch > Web
   browser. Name it "Browser". The sentence reads "If right click over the taskbar and Ctrl held, then Web browser". Close.
3. The line shows "Browser: If right click ..." with its switch now usable; switch it on (Space). Click somewhere outside OmniControl,
   then hold Ctrl and right-click the taskbar: the default browser opens, no taskbar menu. Right-click the taskbar without Ctrl: the
   normal menu. Click twice fast: the second is held back by the 1 s rate limit.
4. Edit: click the "and" block: it becomes "or" (now Ctrl+right click anywhere, or a right click over the taskbar, opens the browser;
   with "or", a right click over the taskbar with no keys held also fires). Flip it back. Remove "Ctrl held" with its ×, and with
   Delete on a focused block.
5. Script rule: Add rule > trigger Keyboard > Key or shortcut: type Ctrl+Alt+F9, Use this trigger. "+ action" > Commands and timing >
   Run a PowerShell or CMD command: Command `New-Item -ItemType File -Force "$env:TEMP\omni-rule-test.txt"`, Use this action. The
   "Allow this command?" window shows the exact command line; Allow. Switch the rule on, press Ctrl+Alt+F9 outside OmniControl: the
   file appears in %TEMP%, no window flashes. Change the command (e.g. another file name): it asks again. Try `exit 3`: the line gets
   "Last run failed", the editor shows it with "Copy details", `rules-log.txt` has the line.
6. Wait and sequence: "+ then" > Wait (1000 ms), then another action: they run in order. Test runs them once.
7. Conflicts: switch on the built-in "Middle click over a screen area: Taskbar" gesture and make a rule "middle click over the
   taskbar": the rule line says "Overrides a gesture"; the rule's action runs, not the gesture's.
8. Mouse page and Gestures cards: their pickers look and work as before (search, categories, cogwheel settings, "Use this action").
9. Both themes, the narrowest window, search for "custom rule" and a rule's name; "Toggle gestures" turns rules off too.

## Audit of the uncommitted work and publish check (13 Nov) - compiled only (throwaway build, 0 warnings, 0 errors); nothing run

Checked by grep and reading against AGENTS.md rules 1-16, the design principles, the audit lessons and the master-list rules.
Fixed in this pass:
- **Mouse hook hot path** (`Core/Engine/Mouse/MouseHook.cs`): the hook data is read in place (`Marshal.PtrToStructure` boxed a copy on every input, every movement while a mode watches it), and the routine log lines ("hook sees ...", "... swallowed") are only formatted while the log still wants lines (`Wants`); before, a string was built for every wheel notch and every taken input even after the log limit.
- **One home for the pipe's sizes:** `CoreTiming.PipeSamplesPerMs` and `PipeRingSamples` replace three copies of the same arithmetic in `AudioPipe`, `PipeOutputs` and `PipeCapture` (same values).
- **Colours:** the meter's green and amber (`Palette.MeterGreen` / `MeterAmber`, look unchanged) and the OSD's light choice (`Palette.LightHex`, typed twice in `OsdOptions`) moved to `Palette`; `LevelMeter`'s height is a named constant.
- **Docs:** `ARCHITECTURE.md` now names every file (the 28 missing ones added: other pages, `HotkeyRow`, `VolumeGroup`, `GestureOptions`, `CursorSpeed`, `PointerLook`, `ClickThrough`, `WebPage`, `Swatches`, `ISideNavigable`, `ClipboardText`, `WindowDrag`, `IMonitor`, `Dxva2Native`); stale names in the open items (`FxChainView`, `MixerRow`, the three row types' FX wiring, now one `VolumeRow`) corrected; the persistence list names every json and log file; `AGENTS.md` honest-reporting line matches the engine (microphone chains are stored only in Fast mode; VST2 is refused with a reason); `README.md` describes the sound modes and the Mouse/Gestures pages; the third-party READMEs and `NOTICE.txt` use the current mode names ("Fast", "Full control").
- **Credits:** `NOTICE.txt` gained the .NET libraries (System.Drawing.Common, System.Management, ServiceController), the ReaEQ design note (no code or image used) and FFTW's exact version (3.3.5, as pinned in `native/EqualizerAPO/README.md`).
- **Personal data:** a scratchpad path with the Windows user name and a monitor model were replaced by generic wording in this file.

Open (not changed, with the reason):
- **Rule 8, App -> Win32:** unchanged from before (`HotkeyService`, `WindowsOsd`, `ClickThrough`). New: `DisplaysPageViewModel` catches `System.Management.ManagementException`, a WMI type, so a page knows Core uses WMI; Core should wrap it in its own failure result.
- **Hook callbacks:** `MouseService`'s handler takes `lock (_gate)` and formats a log line on every mapped press (unmapped input costs nothing); the rule engine, for a rule with a program condition, may call `Process.GetProcessById` inside the hook on a cache miss, and asks `WindowFromPoint` (`OwnProgram.IsUnder`) when a rule listens to the button. All bounded, none on unmapped input; moving them needs a re-test of the mappings.
- **Hook handlers are never removed:** `MouseHook` has no `RemoveHandler`; `RuleEngine` and `GestureService` add handlers that live until the hook is disposed at exit (harmless today, a leak if either is ever re-created).
- **Static caches:** `ProcessIcons.Cache` and `CoreAudio.Meters._meters` (holds COM meter objects, never released) are static mutable state.
- **Bare `catch`:** 14 commented bare catches remain in `CoreAudio.*`, `MixerService`, `ProcessIcons`, `LaptopBrightnessController` (narrowing them to `COMException` may change which failures are survived; needs a device test).
- **Honest wording:** in Fast mode the master's status is "Using your Equalizer APO · no extra delay" with the Running state; AGENTS.md says OmniControl can only say the chain was sent. Owner's call on the words (for example "Sent to your Equalizer APO").
- **Rule hint:** the Gestures page and rule editor say a rule "never runs while OmniControl's window is active or under the pointer"; a shortcut-triggered rule checks only "active" (`RuleEngine.OnShortcut`).
- **Repeated string:** the icon address `avares://OmniControl/Assets/OmniControl.ico` is typed in six windows and `TrayService`.
- **Size:** `Styles/Theme.axaml` is 695 lines, at the 700 limit: split it next time (for example the Volume and FX styles into their own file, as `Blocks.axaml`).

Publish check (nothing pushed or committed):
- No user name, profile path, email, token, machine name, cookie, screenshot or settings file is left in the working tree. The commit identity is the generic "OmniDial Dev <dev@omnidial.local>".
- The owner's GitHub handle appears in `NOTICE.txt` (TabForge's address) and in the already committed `HANDOVER.md` of commits 171cad9 and 9148e3d: intended if the repository is published under that account.
- Ignored and present locally only: `third_party/equalizerapo/EqualizerAPO-x64-1.4.2.exe`, every `bin/` and `obj/`; `.gitignore` also covers `native/EqualizerAPO/deps/` and `build-x64/`, `OmniControl.App/apo/`, the VB-CABLE package, and the fork's own `.gitignore` its build folders. Both `third_party` folders carry a README saying how to get the installer.
- Most of the tree (368 files, among them `LICENSE`, `NOTICE.txt`, `native/`, `third_party/*/README.md`) is untracked and the index holds a partly staged rename (`FxChainView` -> `FxChainPanel`): add everything before the commit that is pushed.
- Licences: `LICENSE` is the GPL-3.0 text; the fork keeps `native/EqualizerAPO/License.txt` (GPL-2.0) and its source headers, changed files say so under their copyright line, libHybridConv and aeffectx.h keep their headers. Four fork files have no licence header (`EqualizerAPO/resource.h`, `helpers/ScopeGuard.h`, `helpers/UncaughtExceptions.h`, `version.h`); not compared with upstream. A release that ships the APO DLLs must add libsndfile's `COPYING`, FFTW's `COPYING` and muparserx's licence beside them (they are downloaded at build time, not in the repository).

## Mouse and Trackpad page: trackpad gestures (13 Nov) - plan and feasibility, written before coding (as-built notes in the next section)

**Feasibility (read from Microsoft's documentation and two open-source utilities; nothing run on a touchpad).**
- **What Windows does with them.** A precision touchpad (PTP) is a HID digitizer (usage page 0x0D, usage 0x05). Windows' own input stack turns its contacts into pointer movement, two-finger scroll (sent to programs as wheel / horizontal wheel), pinch (for programs that do not handle touchpad input themselves: wheel with Ctrl), and three/four-finger swipes and taps, which Windows runs itself (switch apps, show desktop, switch desktops, volume...). The low-level mouse hook (WH_MOUSE_LL) sees the pointer movement and the scroll, never the fingers, so three/four-finger swipes and pinches cannot be detected there.
- **The route: Raw Input.** A window can register for the touchpad's own HID reports with `RegisterRawInputDevices` (usage page 0x0D, usage 0x05, `RIDEV_INPUTSINK` so they arrive while OmniControl is in the background, `RIDEV_DEVNOTIFY` for a touchpad that comes or goes). Each `WM_INPUT` carries one or more HID reports; the device's preparsed data (`GetRawInputDeviceInfo` RIDI_PREPARSEDDATA) and the `HidP_*` functions read them: Contact Count (0x0D/0x54, in the top collection), and per finger collection Contact ID (0x0D/0x51), Tip Switch (0x0D/0x42, through `HidP_GetUsages`), X and Y (0x01/0x30, 0x31) with their logical ranges from `HidP_GetValueCaps`. This is how the open-source samples RawInput.Touchpad (emoacht, C#) and TouchpadGestures Advanced (kamektx, C++) read contacts. Things they and Microsoft's PTP collection spec point out, and that OmniControl must handle: (1) "hybrid" touchpads report the contacts of one frame over several reports (Contact Count is set in the first and 0 in the rest), so contacts are gathered until the count is reached; (2) for a PTP, `RAWINPUTHEADER.hDevice` can be null (Windows filters PTP input before WM_INPUT), so the touchpads found at start-up (`GetRawInputDeviceList`) are kept and a null handle uses the one found; (3) X/Y logical ranges differ per device, so positions are normalised.
- **Recogniser.** A small pure class (Core) turns frames of contacts into one gesture per touch: fingers on the pad (tip switch) = the session's finger count; when that count rises the start positions are taken again; 3 or 4 fingers: the fingers' average position must move far enough (a share of the pad) and mostly along one axis -> up/down/left/right; exactly 2 fingers: the distance between them must shrink or grow by a set share -> pinch in / pinch out (two-finger scroll keeps the distance, so it is not a pinch). One gesture per touch (fingers must all lift before the next), plus a cooldown (`Timing.TrackpadGestureCooldown`). Thresholds are named constants in the recogniser; nothing allocates per report.
- **Windows still does its own gesture.** Raw input is a copy: OmniControl cannot stop Windows' own three/four-finger action or a program's pinch zoom, and must not change Windows' setting itself. It reads (only) `HKCU\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad`: `ThreeFingerSlideEnabled`, `FourFingerSlideEnabled` (0 = "Nothing"; missing = Windows' default, which is in use), `ZoomEnabled` (pinch to zoom; 0 = off, missing or other = on) and `Status\Enabled` (touchpad off). On this PC the slide values are missing (Windows' defaults in force), `ZoomEnabled` is on, one "ELAN PrecisionTouchpad" HID collection is present. A card with an action whose Windows gesture is on says so in one short tag ("Windows also does this", detail in its tooltip), and the group has a "Windows touchpad settings" card whose button opens `ms-settings:devices-touchpad`. No registry writes. Exact meanings of non-zero values (1 = switch apps, 2 = switch desktops, 3 = audio, Custom) differ between sources and are not needed: anything but 0 counts as "Windows uses it".
- **Pinch and Ctrl+wheel.** Windows' pinch reaches programs either through their own touchpad handling (no wheel at all) or as a wheel message with Ctrl; whether that wheel carries `LLMHF_INJECTED` in the mouse hook is not documented. OmniControl therefore does not try to tell it apart in the hook: pinch is recognised only from the raw contacts (two fingers, distance change), which is unambiguous, and a real Ctrl+wheel is never taken for a pinch. Consequence (said in the tooltip): the program under the pointer may still zoom unless "Pinch to zoom" is off in Windows; and the Mouse Buttons "Wheel up/down" cards may also see Windows' synthetic pinch wheel (unchanged from today).
- **Not detectable / unknowns.** No PTP (an old Synaptics/ELAN "mouse-class" driver, or no touchpad): the cards are greyed with "No precision touchpad found" and nothing runs. Windows refusing the registration: greyed with the error. Unverified until the owner tests: that `WM_INPUT` with `RIDEV_INPUTSINK` keeps arriving while another program (or an elevated one) is in front, and while Windows is running its own three/four-finger gesture; the thresholds' feel on this ELAN pad.

**Design (decided from the repo and the brief).**
- **One input path.** Each trackpad gesture is a new value of `Core/Engine/Mouse/MouseButton` (an input the remapper sees, with no release, like a wheel notch). The touchpad watcher hands a recognised gesture to the shared `MouseHook` (`Raise`), which runs it on the hook's own thread through the same handlers in the same order: custom rules, then wheel gestures (ignore it), then `MouseService` (layers, mapping, runner, card flash). So mappings, layers, the action runner and its launch limiter, rule dispatch and rule conflicts need no second copy. Nothing can be swallowed (raw input is a copy); the handler's answer only stops the later handlers (a rule wins over a mapping, as for buttons).
- **One master list of trackpad gestures:** `Core/Engine/Trackpad/TrackpadGestures.All` (button value, stable id, card name, sentence phrase, finger count, motion, which Windows setting also uses it). Read by the recogniser, the page (one card each), the rule triggers (`RuleTriggers` adds one "Trackpad" trigger per entry, kind Press, id `trackpad.<id>`; no new `TriggerKind` is needed because the gesture is its own button value), and the "Windows also does this" check.
- **Storage:** in `mouse.json` with the buttons, keyed `layer.ThreeFingerUp` etc. (the enum names are the stored keys and must not be renamed); existing keys and ids are untouched. New `MouseSettings.TrackpadEnabled` (default on: with every card at "Default" nothing changes). The page rename keeps every id: hotkey ids `mouse.*`, group key `Mouse/Buttons` (only its shown name becomes "Mouse Buttons"), new group key `Mouse/Trackpad`.
- **Layers** apply exactly as for buttons: the layer drop-down on the Mouse Buttons header edits both groups, "Clear layer" and the mapping count include gestures. A gesture has no release, so actions that last while a button is held (hold for layer, slow pointer while held, movement as scroll, axis locks while held, drag/size window, reveal desktop, double-click drag) are greyed in its picker with the reason; the same applies to Wheel and Tilt, which had the same problem (such an action would stay on until the next notch), and `MouseService` refuses to start one for an input without a release (protects saved mappings).
- **Switches:** the page's main switch (renamed "Remap mouse and trackpad") still gates every mapping; a new "Trackpad gestures" switch (first card of the Trackpad group, hotkey "Toggle trackpad gestures", `mouse.trackpad.toggle`) starts and stops the touchpad watcher, so when off no touchpad input is read at all and trackpad rule triggers stop too. Rule triggers also follow the Gestures page's "Enable gestures" switch like every rule.
- **Files:** Core: `Engine/Trackpad/TrackpadGestures.cs` (master list), `GestureRecogniser.cs`, `TouchpadDevice.cs` (one touchpad's HID layout and report reading), `TrackpadWatcher.cs` (hidden message window thread, registration, device list), `RawInputNative.cs` (user32 + hid P/Invoke), `WindowsTouchpad.cs` (the registry read); `MouseHook.Raise`; `MouseButton` values and `MouseButtons.HasRelease`. App: `Mouse/TrackpadService.cs` (owns the watcher, its state and Windows' settings, feeds the hook), `MouseSettings.TrackpadEnabled`, `MouseActionCatalog` (held-only actions greyed for inputs without a release), `Timing.TrackpadGestureCooldown`, `RuleTriggers` entries, page partial `Pages/Mouse/MousePageViewModel.Trackpad.cs`, `MousePage.axaml` group, `MouseButtonItem` / `MouseButtonRow` (availability and the Windows tag).

## Mouse and Trackpad page: trackpad gestures (13 Nov) - as built; compiled only (throwaway builds, 0 warnings, 0 errors); nothing run, seen or touched on a touchpad

Built to the plan above (file map in `ARCHITECTURE.md`: "Trackpad gestures" master list, "Trackpad gestures, Windows side / app side", the Mouse and Trackpad page row). Differences and things to know:
- **Page:** title "Mouse and Trackpad" (sidebar, search category, the hotkeys' heading and the category filter all read the page's `Title`); the main switch is now "Remap mouse and trackpad" (it gates both groups); "Buttons" is shown as "Mouse Buttons" (key `Mouse/Buttons` kept). Trackpad group (opens by default the first time): "Trackpad gestures" switch + "Windows touchpad settings" (Open -> `ms-settings:devices-touchpad`) as a half-width pair, then the ten gesture cards in pairs (three up/down, left/right, four up/down, left/right, pinch in/out). A card lights up when its gesture is recognised (the same flash as a mouse button), mapped or not, unless a custom rule took it: an easy way for the owner to see recognition working.
- **Hand-written page names reworded** so they no longer name the page: the Gestures page hint and the rule editor hint ("the mouse and trackpad mappings"), the rule conflict detail ("Your mouse and trackpad mappings give this input an action on <layer>"); the layer manager counts "N mappings" (buttons and gestures) instead of "N buttons"; the Mouse page hint now says the left and right buttons are never remapped over OmniControl's window (it said the whole window, which was wrong since 12 Nov) and explains the trackpad. README updated.
- **Behaviour change for Wheel and Tilt:** their pickers now grey the "while held" actions (hold for layer N, slow pointer while held, movement as scroll, axis locks while held, drag/size window, reveal desktop, double-click drag) with "Needs a button that is held down and let go", and `MouseService` ignores such a mapping saved earlier on any input without a release (before, it stayed on until the next notch of that wheel direction: a held layer or held keys could stick). `MouseActionCatalog.IsWhileHeld` replaced `HasPointerLook` (same set plus hold-for-layer).
- **Shared pieces:** `MouseButtons.HasRelease` (Core) replaced three private copies (`MouseHook.IsWheel`, `RuleEngine.IsPressable`, `MouseService`'s inline test); `MouseHook.RunHandlers` is the one handler loop for hook and raised inputs (its log line says "taken" instead of "swallowed").
- **Hot path:** per `WM_INPUT`: one `GetRawInputData` into a reused unmanaged buffer, a dictionary lookup, per finger 2-3 `HidP_*` calls with a stack buffer, the recogniser on fixed fields; a recognised gesture is one `PostThreadMessage`. Logging only on start, stop, device change and errors (`mouse-log.txt`).

**Verified by reading code (not run):** the gesture list is one registration (`TrackpadGestures.All`) read by the recogniser, the page's cards, `RuleTriggers` (a "Trackpad" category with ten triggers, between "Press and hold" and "Keyboard") and the Windows tag. The new action "Toggle trackpad gestures" (`mouse.trackpad.toggle`, heading "Mouse and Trackpad") is a `HotkeyRegistry.Register`, so it appears on the Shortcuts page, in every card's picker and the Gestures middle-click picker (`MouseActionCatalog.Choices` over the live registry), in the rule step list (`RuleSteps.Choices`), and the sidebar search finds the page by its keywords; the two new groups' cards are search entries (group "Mouse Buttons" / "Trackpad"). Saved data: hotkey ids, `mouse.json` keys and the Buttons group key are unchanged; the Shortcuts page's group for this page is keyed by heading ("Shortcuts/Mouse and Trackpad"), so its remembered open/closed state starts fresh once.

**Not verified (nothing was run):** that Windows delivers the touchpad's reports to OmniControl (background, another program or an elevated one in front, while Windows runs its own swipe); the ELAN pad's report layout (hybrid or parallel, tip switch present, logical ranges) and a null `hDevice`; the thresholds' feel (`GestureRecogniser.SwipeDistance` 12 % of the longer side, `SwipeDominance` 1.5, `PinchChange` 25 %, cooldown 300 ms); the cards, tag and greyed look on screen, at 720 px and in both themes; whether Avalonia itself registers digitizer raw input (one registration per usage per process: if it did, one of the two would stop receiving).

**Open items.**
- Palm rejection: the Confidence usage (0x0D/0x47) is not read, so a resting palm counts as a finger (a palm plus two fingers could read as three). Read it when the owner sees false three-finger swipes.
- Two precision touchpads at once would mix their contacts in one recogniser (rare).
- Pinch with an action still lets the program zoom unless Windows' "Pinch to zoom" is off; and Windows' synthetic Ctrl+wheel of a pinch may also reach the Wheel up/down cards and wheel gestures (not told apart; see the plan).
- Three/four-finger taps are not offered (not in the brief); they would be new entries in `TrackpadGestures` plus a tap rule in the recogniser.
- Windows' "Custom" three/four-finger setting counts as "Windows uses it" even if a single direction is set to nothing.

**Owner test steps.**
1. Build and launch. Sidebar: "Mouse and Trackpad". The page: "Remap mouse and trackpad" switch, the "Mouse Buttons" group (layer drop-down and cogwheel on its header), the "Trackpad" group below it, open, with the switch, "Windows touchpad settings" and ten cards. Search "trackpad", "pinch", "three fingers": the cards show under "Mouse and Trackpad - Trackpad".
2. With every card at "Default": swipe three fingers up, down, left, right, then four fingers, then pinch in and out. Windows does what it always did, and each matching card lights briefly (recognition works). If no card lights, `mouse-log.txt` says whether the touchpad watcher started and how many touchpads it found.
3. Set "Three fingers up" to an action (for example Media > Play / pause). With Windows' three-finger swipes on, the card shows "Windows also does this"; swiping up runs both Windows' action and play/pause. Click "Windows touchpad settings" > Open, set three-finger swipes to Nothing, come back to the page: the tag is gone, and the swipe now runs only play/pause.
4. Four fingers: same as step 3 with a four-finger card and Windows' four-finger setting.
5. Pinch: map "Pinch in" to Volume down and "Pinch out" to Volume up; pinch over a browser: the volume changes once per pinch (lift and pinch again for the next step); the browser also zooms while "Pinch to zoom" is on in Windows; turn it off in Windows and it no longer does.
6. Layers: switch the Layer drop-down to layer 2: the trackpad cards show layer 2's actions; a gesture uses the layer in force; "Manage layers" counts gesture mappings as mappings.
7. Wheel/Tilt and gesture pickers: "Hold for layer 2", "Slow cursor while held" etc. are greyed with the reason; the mouse buttons' pickers still offer them.
8. "OmniControl trackpad gestures" switch off (or its hotkey from Shortcuts; the switch only stops OmniControl's own recognition, Windows' gestures are unaffected): no card lights on a swipe; on again: they do.
9. Custom rules: Gestures page > Add rule > "+ trigger" > Trackpad > Three fingers up; add an action; the sentence reads "If swipe three fingers up, then ..."; switch it on: the swipe runs the rule (and not the card's mapping; the rule line says "Overrides a mouse mapping" if the card has one).
10. Both themes and the narrowest window: the tag and card labels fit; on a PC without a precision touchpad the ten cards are greyed with "No precision touchpad found..." as their tooltip.

## Mouse and Trackpad page: Pointer settings (13 Nov) - plan, written before coding (as-built notes in the next section)

**What and where.** A third group "Pointer settings" (key `Mouse/Pointer`) under Mouse Buttons and Trackpad: Windows' own mouse settings, changed live (no Apply), with the real current value shown. Layers: Windows access (`SystemParametersInfo`, `GetSystemMetrics`, `GetDoubleClickTime`, the Cursors registry) in `Core/Engine/Pointer`; backup, write queue, swap trial in an App service (`App/Mouse/PointerSettingsService`); the master list of cards and the page group in `Pages/Mouse`.

**Settings and the Windows call behind each (from the SystemParametersInfo documentation; every write uses SPIF_UPDATEINIFILE | SPIF_SENDCHANGE like Windows' own panel, so it survives a restart).**
| Setting | Read | Write | Range, Windows default |
|---|---|---|---|
| Pointer speed | SPI_GETMOUSESPEED (0x70, pvParam -> int) | SPI_SETMOUSESPEED (0x71, value in pvParam) | 1-20, 10 |
| Enhance pointer precision | SPI_GETMOUSE (0x03, int[3]: threshold 1, threshold 2, acceleration) | SPI_SETMOUSE (0x04, int[3]); on = the thresholds kept (6, 10 when they were 0) and acceleration 1, off = 0, 0, 0 (what the control panel writes) | on (6, 10, 1) |
| Swap primary and secondary buttons | GetSystemMetrics(SM_SWAPBUTTON) | SPI_SETMOUSEBUTTONSWAP (0x21, uiParam) (not `SwapMouseButton`, which is not saved) | off |
| Double-click time | GetDoubleClickTime | SPI_SETDOUBLECLICKTIME (0x20, uiParam ms; Windows caps at 5000) | 200-900 ms (the control panel's range), 500 |
| Lines per wheel notch | SPI_GETWHEELSCROLLLINES (0x68) | SPI_SETWHEELSCROLLLINES (0x69, uiParam) | 1-100, 3 |
| Scroll one screen at a time | same value = WHEEL_PAGESCROLL (UINT_MAX) | writes WHEEL_PAGESCROLL, or back to the lines last seen | off |
| Characters per sideways notch | SPI_GETWHEELSCROLLCHARS (0x6C) | SPI_SETWHEELSCROLLCHARS (0x6D, uiParam) | 1-100, 3 |
| Pointer trails / trail length | SPI_GETMOUSETRAILS (0x5E; 0 or 1 = off, n > 1 = n pointers) | SPI_SETMOUSETRAILS (0x5D, uiParam) | off; length 2-7 (the panel's range), 7 when first turned on |
| Hide pointer while typing | SPI_GETMOUSEVANISH (0x1020) | SPI_SETMOUSEVANISH (0x1021, BOOL in pvParam) | on (as Windows ships; the documentation says off) |
| Snap to the default button | SPI_GETSNAPTODEFBUTTON (0x5F) | SPI_SETSNAPTODEFBUTTON (0x60, uiParam) | off |
| Show pointer location when Ctrl is pressed | SPI_GETMOUSESONAR (0x101C) | SPI_SETMOUSESONAR (0x101D, BOOL in pvParam) | off |
| ClickLock | SPI_GETMOUSECLICKLOCK (0x101E) | SPI_SETMOUSECLICKLOCK (0x101F, BOOL in pvParam) | off (its delay is not offered) |
| Pointer scheme | `HKCU\Control Panel\Cursors` (Default) + "Scheme Source" + the 17 role values, matched against the schemes | the role values, (Default) and Scheme Source written as the Mouse control panel does, then SPI_SETCURSORS (0x57) with both flags | schemes from `HKLM\...\Control Panel\Cursors\Schemes` (system; display name from the `@main.cpl,-N` resource) and `HKCU\Control Panel\Cursors\Schemes` (user); default = system scheme "Windows Aero" (shown by Windows as "Windows Default") |
| One pointer per role (17 roles in the schemes' fixed order: Arrow, Help, AppStarting, Wait, Crosshair, IBeam, NWPen, No, SizeNS, SizeWE, SizeNWSE, SizeNESW, SizeAll, UpArrow, Hand, Pin, Person) | the role's registry value | a .cur/.ani from the file picker (checked with LoadCursorFromFile first), or "Default" = the default scheme's file; then SPI_SETCURSORS | |

**Dropped, and why.**
- **Pointer size and colour** (`HKCU\Software\Microsoft\Accessibility` CursorSize/CursorType/CursorColor, `Cursors\CursorBaseSize`): undocumented; Windows Settings generates coloured pointer files itself and the registry values alone do not recolour or reliably resize. A card opens Windows' own page (`ms-settings:easeofaccess-mousepointer`) instead.
- **Scroll inactive windows when hovering**: SPI_GETMOUSEWHEELROUTING/SET (0x201C/0x201D) are documented with only 0 (focus) and 1 (hybrid), and the documentation contradicts itself on whether the setter takes the value or a pointer; the Settings switch uses a third value (2) that the page does not list. Writing it cannot be made correct without trying it on the PC, so a card opens `ms-settings:mousetouchpad`.
- ClickLock's delay, double-click width/height, hover time: not asked for.

**Data model.** Core: `PointerValue` (enum, stored by name in the backup: never rename) with `PointerValues.Info` (Windows' range and default, defined once); `PointerSettings` (Read/Write of a `PointerValue`, `persist` flag); `CursorRoles.All` (the 17 roles, registry name and Windows' own label); `CursorSchemes` (list, current, apply, set one role, snapshot/restore). App master list `Pages/Mouse/PointerControls.All`: one `PointerControl` per card (stored id, label, kind Number/Switch/Choice, the Core value, step, unit, keywords, the noun used in action titles, optional wheel level id, optional "needs another switch on/off"); the cards, their search entries, the actions and the OSD element are all made from it.

**Backup and restore.** Before OmniControl's first write, `pointer-settings-original.json` gets every value and the cursor registry snapshot (scheme name, source, each role value and whether it existed). It is written once and kept. Cards at the bottom: "Reset to Windows defaults" and "Restore my original values" (pills). Swapping the buttons applies at once, so a change of the swap (from the card, a hotkey, a mouse button, a rule, or a reset) is a trial: written for this session only (no SPIF_UPDATEINIFILE), a "Keep the swapped buttons?" message opens with Keep focused (Enter keeps it; the other physical button now clicks), and unless Keep is chosen within `Timing.PointerSwapKeep` (15 s) it is put back; a journal `pointer-swap-trial.json` puts it back at the next start after a crash, and an unsaved swap also ends at sign-out. Restoring the original values does not ask (it returns to what the owner already used).

**Actions (master list, from the entries).** Number: "<name> up/down" by the entry's step; pointer speed is a level (`HotkeyAction.ForLevel`, level id `pointer` = the stored id of the Gestures page's old "Pointer speed" wheel step, which is removed so there is one action; a gesture's percent is a share of Windows' 1-20 scale, 5 % = 1) with an OSD element "Pointer speed". Switch: "Toggle <name>". Scheme: next/previous. Plus "Reset pointer settings to Windows defaults" and "Restore my original pointer settings". Heading "Mouse and Trackpad - Pointer settings".

**Reading.** On page open and when the window comes on screen (no polling); after every OmniControl write; a write Windows refuses shows one plain sentence and the card goes back to the real value.

**Files.** Core `Engine/Pointer/`: `SpiNative.cs` (the one SystemParametersInfo declaration; `SystemCommands` and `PointerShape` use it), `PointerValue.cs`, `PointerSettings.cs`, `CursorRoles.cs`, `CursorSchemes.cs`. App: `Mouse/PointerSettingsService.cs`, `Mouse/PointerBackup.cs`, `Mouse/SwapTrial.cs`; `Pages/Mouse/PointerControls.cs`, `Pages/Mouse/MousePageViewModel.Pointer.cs` + `.PointerActions.cs`; shared card `Pages/FileItem.cs` + `FileRow.axaml(.cs)`; `EngineMessage.Dismiss` (close a message from code); `LevelStepper.Step` takes a range; `Percent.OfRange`; `Timing.PointerSwapKeep`.

## Mouse and Trackpad page: Pointer settings (13 Nov) - as built; compiled only (throwaway builds, 0 warnings, 0 errors); nothing run, no Windows setting changed (only read-only registry queries)

Built to the plan above (file map in `ARCHITECTURE.md`: "Pointer settings" and "Pointer roles" master lists, "Windows' pointer settings, Windows side / app side", the Mouse and Trackpad page row). Differences and things to know:
- **Cards (half width, two per row, in this order):** Pointer speed | Enhance pointer precision; Swap left and right buttons | Double-click time; Lines per wheel notch | Scroll one screen at a time; Characters per sideways notch | ClickLock; Pointer trails | Trail length; Hide pointer while typing | Show pointer location on Ctrl; Snap to default button | Pointer scheme; Pointer size and colour (Open) | Scroll inactive windows (Open). Inner group "Pointers" (collapsed): one file card per role (name, file in use or "Built-in pointer", Choose…, Default). Then Reset to Windows defaults | Restore my original values (greyed with the reason until OmniControl has changed something). "Lines per wheel notch" is greyed while "Scroll one screen at a time" is on, "Trail length" while trails are off.
- **Swap decision:** every swap is a trial (card, hotkey, mouse button, gesture, rule, reset): written for this session only, a "Keep this button swap?" message with Keep focused (Enter keeps), put back after 15 s (`Timing.PointerSwapKeep`), on Cancel/Esc/X, at exit, and at the next start after a crash (`pointer-swap-trial.json`); an unsaved swap also ends at sign-out. Why: a swap applies instantly and makes the familiar button do the other thing, so a wrong one must undo itself without a click. "Restore my original values" writes the swap without a trial (it goes back to what the owner already used).
- **Shared pieces added or changed:** `Pages/FileItem` + `FileRow` (shared file card); `ToggleItem.Load` (shows a state without applying it, as `SliderItem.Load` and `ChoiceItem.Load` do, so reading Windows never writes); `EngineMessage.Dismiss` (a message closes when its token is cancelled); `LevelStepper.Step` takes an optional range (default 0-100, unchanged for Displays and Volume); `Percent.OfRange` (the Displays `LevelSpec.OsdPercent` now uses it); `Core/Engine/Pointer/SpiNative` is the one SystemParametersInfo declaration (`SystemCommands` and `PointerShape` lost their private copies; `SystemCommands.MinSpeed/MaxSpeed` now come from `PointerValues`). The unused `MousePageViewModel.Items` was removed.
- **One action instead of two:** the Gestures page's own wheel step "Pointer speed" (id `pointer`, session-only speed change, its own "Pointer speed change per wheel notch" option) is removed; the Pointer settings' level has the same id `pointer`, so a saved wheel gesture keeps working and now changes the saved Windows speed. Its old per-notch number is not read: a notch is the gesture's "Change per wheel notch" percent of Windows' 20 speeds (default 5 % = 1 speed). The Mouse page's "Cycle cursor speed" and "Slow pointer" built-in actions are unchanged (they change the speed for the session only, on purpose).
- **Actions (heading "Mouse and Trackpad - Pointer settings"):** `pointer.speed.up/down` (level `pointer`, OSD "Pointer speed": the pop-up shows the speed as a 0-100 share of 1-20, as other levels show theirs), `pointer.doubleclick.up/down` (10 ms), `pointer.lines.up/down`, `pointer.chars.up/down`, `pointer.traillength.up/down` (1 each; refused with the card's reason while it does not apply), `pointer.precision|swap|screen|clicklock|trails|vanish|sonar|snap.toggle`, `pointer.scheme.next/previous`, `pointer.reset`, `pointer.restore`. Verified by reading code: they are `HotkeyRegistry.Register` entries, so they appear on the Shortcuts page (group "Mouse and Trackpad - Pointer settings"), in the mouse button, trackpad and middle-click pickers (`MouseActionCatalog.Choices` over the live list), the rule step list (`RuleSteps.Choices`), and the sidebar search (`ShellViewModel.Matches` reads action keywords); the speed level is offered by the wheel gesture picker (`GestureCatalog.Choices`) and run by `HotkeyRegistry.RunLevel`. Every card is a search entry under "Mouse and Trackpad - Pointer settings".
- **Not verified (nothing was run):** that each SystemParametersInfo write takes effect live and is kept after a restart; the pvParam-or-uiParam use of each setter (taken from the documentation, see the plan); ClickLock and Ctrl-location behaviour; the scheme display names from `main.cpl` resources; that Windows' Mouse control panel shows a scheme OmniControl applied as selected (OmniControl writes the display name, e.g. "Windows Default", as this PC has it); whether the low-level mouse hook and the Mouse Buttons mappings follow the physical or the swapped buttons after a swap; the cards on screen at 720 px and in both themes (labels were shortened so they wrap to two lines at most); the message window while the main window is in the tray.

**Open items.**
- "Scroll inactive windows" and pointer size/colour are links to Windows' pages (no reliable documented way to set them; see the plan).
- Changes made in Windows' own settings while this page is on screen show when the page is opened again or the window comes back (no settings-change listener; no polling, as asked).
- Writes run on a worker and reads do not wait for them: a card reloaded right after a change (a reset followed by the swap question) can show the old value until the write's own refresh, and a reset that puts back a swap on trial may then ask once more about the default swap.
- `Fx/IrFileItem` + `IrFileRow` could become a `FileItem` (same picker pattern); left so the FX editor's look is unchanged.
- The double-click card's slider runs 200-900 ms (the control panel's range); a value set outside it elsewhere sits at the slider's end, the number shows the real value.

**Owner test steps.**
1. Build and launch. Mouse and Trackpad: a third group "Pointer settings" (collapsed the first time) below Trackpad; open it. Each card shows what Windows shows (compare with Settings > Bluetooth & devices > Mouse and the classic Mouse control panel, `main.cpl`). "Restore my original values" is greyed.
2. Move Pointer speed: the pointer changes at once; reopen the page: same value; Windows' Settings shows it too, and it stays after a restart. Wheel over the slider, double-click it (back to 10), type a value (double-click the number).
3. Toggle Enhance pointer precision, Hide pointer while typing, Show pointer location on Ctrl (press Ctrl: rings), Snap to default button, ClickLock, Pointer trails (Trail length becomes usable). "Restore my original values" is now available.
4. Swap left and right buttons: "Keep this button swap?" opens with Keep focused. Do nothing for 15 s: the buttons go back by themselves and the switch turns off. Swap again and press Enter: kept. Swap back the same way.
5. Pointer scheme: pick "Windows Black (system scheme)": the pointers change. Open "Pointers", choose a .cur file for Normal select: the scheme card shows "Custom"; click Default on that card: back.
6. Reset to Windows defaults: everything goes to Windows' defaults and "Windows Default"; if your buttons were swapped, the swap question appears. Then "Restore my original values": every value and pointer is back as before step 2.
7. Shortcuts page: "Mouse and Trackpad - Pointer settings" lists the actions; bind "Pointer speed up" and press it: the OSD shows "Pointer speed". Gestures page: a wheel gesture can pick "Pointer speed" under the same heading.
8. Both themes and the narrowest window: labels wrap to two lines at most; search "pointer", "scroll", "swap", "trails".

**If something goes wrong:** "Restore my original values" (or the action "Restore my original pointer settings") puts everything back from `%APPDATA%\OmniControl\pointer-settings-original.json`. Swapped buttons you can't use: wait 15 s (or press Esc) and they go back; if OmniControl closes meanwhile they go back at exit, at the next start, or at sign-out. Without OmniControl: Settings > Bluetooth & devices > Mouse (primary button, speed, scrolling), Settings > Accessibility > Mouse pointer and touch, and `main.cpl` > Pointers > Scheme "Windows Default (system scheme)" > Apply.

## Effects method shown on rows; FX window crash and slow open (13 Nov) - compiled only (throwaway build, 0 warnings, 0 errors); nothing run or seen

**Effects method.** `SoundMode` now carries `Master` and `Program` (`ModeMethod`: `How` + `Delay`, one entry per mode in `SoundModes.cs`; the 30-80 ms figure is one constant). `EngineStatus.LineFor(mode, master)` builds the line of the FX window and the tooltip of a row's status ("Fast mode · effects by recording · Effects running · about 40 ms delay"); a failed or unavailable status is said alone and first, and with effects off the method is only what would happen ("when on: ..."). `SoundMode.MethodSummary` is the extra line on each Sound mode card. A program in Fast mode whose sound cannot be recorded (`SoundModes.CaptureMissing`) gets a caution tag with the reason and a "Switch to Full control" link on its sentence line when it has effects (`VolumeRow.CaptureGate`, a `ModeGate`; the rule is `SoundModeService.ProgramCaptureCaution`, also read by the FX window, which shows the same tag); the Test link sits next to it. The wording lives only in the mode table.

**FX window crash.** Cause: `FxChainPanel.axaml` bound a row's Edit button through `((vm:FxChainViewModel)DataContext)` inside a DataTemplate; this project does not turn compiled bindings on (no `CompileBindings` anywhere), so `x:DataType` does not compile such a binding and the cast was resolved at run time, where the prefix `vm` cannot be resolved inside a template. Now `FxSlotViewModel.EditCommand` (set up by the chain through `EditRequested`) needs no cast. The same pattern in `Controls/OptionList.axaml` (`((c:OptionListModel)DataContext).Multiple`) is replaced by `OptionEntry.IsSingleChoice`, set by `OptionListModel.SetItems`. No other cast or `$parent[...]` binding is left in any .axaml. Open item: `AvaloniaUseCompiledBindingsByDefault` is not set, so every `x:DataType` in the project is documentation only and a wrong binding path fails silently.

**Slow open (by reading the code, not measured).** (1) Constructing the chain view-model selected the first effect, which loaded its VST3 plug-in and made its editor on the UI thread before the window was shown (a plug-in load is from tens of ms to seconds). Now selecting a plug-in shows "Loading the plug-in..." and loads it on the next Background dispatch, after the window has drawn (`FxChainViewModel.LoadEditorLater`); it still runs on the UI thread because a plug-in's editor must live on the thread that made it. (2) Every effect's editor cards were built when the window opened (an equaliser builds a graph model and up to 64 band cards each); now built when the effect is first selected (`FxSlotViewModel.EditorItems`). (3) Every chain view-model read and parsed `plugins.json` (every scanned plug-in) even if Add was never used; now `FxChainViewModel.Library` is read on first use.

**Event log (read only).** Today's OmniControl crashes: 7 x `FileLoadException` "An Application Control policy has blocked this file" for the freshly built `OmniControl.dll`, `OmniControl.Core.dll` and `OmniControlApoSetup.dll` in `bin\Relaunch...` folders (Windows App Control / Smart App Control, not a code fault; see the Smart App Control section), and one `ArgumentException` at 17:21 from Avalonia.Markup.Xaml (the cast above). Open item: a new build's DLLs may be blocked at start-up; the app cannot say so because it never starts.

## Effects back in their own FX window; FX window look restored (13 Nov) - compiled only (throwaway build, 0 warnings, 0 errors); nothing run, seen or clicked

> Superseded in part on 15 Nov ("FX window tidy, keyboard instructions off screen", the last sections): the keyboard footer strip, the CPU number, the Output picker in the header and the controls under and above the editor are gone; the toolbar row, the status strip and Options > Plays on / Keyboard shortcuts replace them.

**Owner decision:** a row's effects open in a separate FX window again; the Design D in-row expansion is removed. The rest of the
Volume page (one list, Sound mode, "plays on", Combine devices) is unchanged.

**Volume row.** The effects link ("2 effects", "1 effect, off") and a new `FX` pill (`Button.pill`, where the expand arrow was) open the
row's FX window with one click, Enter, or Space (`VolumeRow.OpenWindowCommand` -> `FxWindowService.Open(key)`; master, programs,
microphones, combined devices). Both are dimmed where the mode can't run that row's effects (microphones and combined devices in
Fast), as the arrow was. No double-click anywhere on the row. Fast mode's recording caution (tag with the reason), "Switch to Full
control" and "Test" (with its result) are on the sentence line, only on a row that has effects; the engine's status is shown after
the sentence unless the caution already says why nothing runs, and its tooltip is the full method line (`EngineStatus.LineFor`).
Removed: `RowEffects.axaml(.cs)`, `Pages/KeyScope.cs` (only the expanded part used it; `RowEditing` and `GroupedCards` no longer
check it), `VolumeRow` expansion state (`IsExpanded`, `Chain`, `MakeChain`, `ToggleExpand`, `CloseChainIfGated`, `IDisposable`,
`ToggleFx`), the page's `makeChain` argument, `VolumeChainMinHeight`, the search style that hid the chain.

**Effects on/off.** One switch: "Effects on / Effects off" (`ToggleSwitch`, the master switch) at the top of the FX window, also
Options > Chain on. The row keeps saying "N effects, off". Quick hotkeys "Toggle master FX" and "Toggle microphone FX" are unchanged.
(A switch on the row was considered; the owner chose the FX window header.)

**FX window, what differed from the committed (pre-rework) window and what was restored.**
- Restored: rows show number, power, the kind ("VST3"/"FX", kind in full as tooltip) and the name; the Edit pill is gone (Enter gives
  a built-in effect's editor the keyboard; a double-click pops any effect out since 14 Nov); the heading "The sound passes these effects in order..." is gone (now the
  number's tooltip); "Forget program" is back in the list's button row after Add and Remove (out of the FX menu; it reaches the row
  through `FxChainPanel.ListTools`, so the panel still knows nothing of the window); the window opens centred on the screen
  (`CenterScreen`, was `CenterOwner`); "Keep on top" (its menu text again; tooltip "Also above other programs").
- Kept from the rework (asked for): embedded editors with Pop out / Put back pills and menu items, numbering, drag of the selection,
  rectangle and Windows selection keys, owned window (stays above the main window), deferred plug-in loading, Copy/Paste, the
  "Runs in ..." note (honest reporting).
- Kept but different: the list is a fixed width (needed by the window-grow maths) at 246 DIPs, which is the old 0.85 : 2 share at the
  window's first size (860); at the narrowest window (560) the editor gets about 280 DIPs.
- Added: the header line has the effects switch, the program's Output, the engine line (now always `EngineStatus.LineFor`, the same
  line as the row's tooltip) and the recording caution tag (`SoundModeService.ProgramCaptureCaution`, one rule for row and window).

**TabForge FX loader comparison** (owner's own code, `Views/Fx/FxChainList.cs`, read only): double-click a row floats a plug-in's
editor (TabForge `RowActivated` -> `Float`) -> added here, and a second double-click puts it back (TabForge only floats; put back is its
notice button, here the Put back pills too); click, Ctrl+click toggle, Shift+click range, Ctrl+A, Shift+arrows (the ListBox's own);
a press on a selected row of a multi-selection keeps it for a drag, a click without moving then selects that row alone (same);
press in the empty part of the list draws the rectangle, Ctrl adds (same; it also starts below the last row, the list fills the pane
with a transparent background); Esc cancels a drag or a rectangle, then reduces the selection to the focused row (rectangle Esc added
now, `RubberBandSelect.Band.Cancel` restores the selection from before the press). Differences left: no edge scrolling while dragging
or drawing in a long list; no right-click menu; TabForge's band selects by height only (any x), this one by the rectangle; Home/End are
the ListBox's own; drag onto containers does not exist here.

**Header "Switch" link (owner's screenshot).** Cause: the header's "Switch" link beside "+ Combine devices" showed whenever the mode
was Fast (Combine devices needs Full control, so the gate always had a reason there), and any link whose text was a plain string
(`Content="Switch"`) had no underline: Avalonia's ContentPresenter draws string content itself, so `Button.link TextBlock` never
reached it. Fixed: the header link is removed (the Sound mode button beside it is where the mode changes; the dimmed Combine button
keeps the reason as tooltip); `Button.link` now gives string content a TextBlock through a `ContentTemplate`, so every link
("Switch", "Switch to Full control", "Test", "Combine devices…") looks the same. The other Switch links (a row's dimmed effects or
"plays on" on microphones and combined devices in Fast, the "plays on" pop-down's several-devices note) show only with a real gate
reason, next to the control they explain. (13 Nov: the header moved to the Wiring tab's top bar; the combined rows' "plays on" Switch and the pop-down's note are
gone; the Wiring cell's menu shows that note and its one Switch.)

**Not verified.** Compiled only. Nothing was run or seen: the row line with caution, Switch and Test at 720 px (the sentence wraps),
the FX pill's width in the shared column, the link template's underline, the FX window header (switch, output card, engine line,
tag) at 560 px and in both themes, double-click on a row with and without a loaded plug-in, Esc during a rectangle, the Forget
program button's binding.

**Open items.** Edge scrolling during drag and rectangle; (settled: a microphone row in Fast shows the "Switch" link beside its effects text only when it has an effects chain; with none it just says "no effects", dimmed (`VolumeRow.ShowsEffectsSwitch`, `InputItem.SwitchNeedsEffects`).)

**Owner test steps.**
1. Volume page: click "1 effect" (one click) on the master, a program and a microphone (Full control): each opens its FX window; a
   second click brings the same window to the front. Keyboard: Up/Down to a row, Left/Right to the link or FX, Enter.
2. FX window: "Effects off" in the header: the row says "1 effect, off"; on again. Options > Chain on follows.
3. Rows show number, switch, VST3/FX, name. Double-click a VST3 row: its editor opens in its own window, the pane says it is open
   on its own; double-click again (or Put back): it returns. (Since 14 Nov a double-click on an Equaliser row pops it out too; Enter gives its cards the keyboard.)
4. Press in the empty space below the last row and drag up over rows: they are selected; press Esc while dragging the rectangle:
   the earlier selection returns. Ctrl+click, Shift+click, Ctrl+A, Shift+Up/Down, drag a multi-selection, Delete.
5. A remembered (closed) program's FX window: "Forget program" sits after Add and Remove.
6. Fast mode on this PC (recording fails): a program with effects shows the yellow tag, "Switch to Full control" and Test on its
   line; its FX window shows the same tag under the engine line. The page header shows no stray "Switch" any more.
7. Both themes, narrowest main window (720) and FX window (560), search results for a program's name (the row shows FX, not ×).

## "Plays on" pop-down redesign (option B, chips on top) - superseded 13 Nov by the Mixer and Wiring tabs

The pop-down below no longer exists (the Mixer's device words open the Wiring tab; the Wiring cell has its own small menu). What it
made lives on: `FlyoutPresenter.listmenu`, `Button.menurow`, `TextBlock.menutext`, `Button.chipx`, `Border.hrule` and the `Menu*` /
`Chip*` sizes (now in `Styles/Menus.axaml`); the `>` list selectors; the `Button.link > TextBlock` underline. Its OptionList chips, tags,
search box and headings were removed with it. Kept for the history:

Narrow pop-down (`FlyoutPresenter.listmenu`, 260-300 px, sized to content; menu look and placement: see the next section; `PlacementConstraintAdjustment="SlideX, FlipY, ResizeY"` keeps it on screen and off the row's link): title line "<Row> plays on", `OptionList` (chips of the ticked devices with an x, only when at least one is ticked, two lines then scroll; a search box only above 6 devices (`SearchAboveDevices` in `VolumePageViewModel.Routing.cs`); 26 px device rows with a tick, left-aligned name and an optional `Border.tag` such as "Full control" on a device this mode can't use, which is dimmed and cannot be ticked), then, only when `SeveralGate` has a reason, one calm sentence (`Gate.Note`, set once in `SoundModes.Check`, e.g. "Fast mode plays on one device.") with one "Switch to Full control" link (`ModeGate.SwitchLabel`), a thin line and "Combine devices..." with a plus.
Generic pieces added to `Controls/OptionList(+Model)`: `HasChips`, `SearchAbove`, `OptionEntry.Tag/ChipLabel/ShowsChip`; group headings only when two or more groups are shown; entries no longer show their reason under the label (tooltip only). Theme: `Button.chipx`, `Border.hrule`, `FlyoutPresenter.listmenu`, `ListBox.pick.compact`; `Button.link`'s underline now applies only to the link's own words (the pop-down hangs under the link in the logical tree and used to inherit the underline and its 220 px MaxWidth, which is why titles, headings and device names were underlined).
**Unverified (nothing run or seen):** the look, the pop-down placement near the screen edge, `ResizeY`, the chip wrap and scroll, the 720 px window, both themes, and the Combine devices window's compact rows.
**Owner test steps.** Open "plays on" on the master, a program and a combined microphone: no underlines except Switch and Combine; tick several devices (Full control) and see chips appear, click a chip's x; in Fast mode the combined speakers are dimmed with a "Full control" tag and the note shows once with one Switch link; with 7 or more devices the search box appears; open it on the last row and near the bottom of the screen; keyboard Up/Down, Space/Enter, Esc, Backspace on an empty search; Combine devices window rows still fine.

## "Plays on" pop-down: menu look (owner's screenshot "These just look horrible") - superseded 13 Nov (see the section above)

Causes (read from the code and Avalonia 11.3.6 sources): (1) the page's row styles were descendant selectors (`ListBox.rows ListBoxItem`), and a pop-down hangs in the logical tree under the control it opens from, so the device rows inside it took the page cards' look: 12,5 padding, a 1 px rounded (10) border and 4 px gap per row, inside the list's own row (the "box in a box"); (2) Avalonia has no selector specificity, so `Button.chipx Path.glyph` (8 px) was overwritten by the later generic `Path.glyph` (14 px), and the "+" was a plain 14 px `Path.glyph`; (3) the device row's Grid gave the hidden icon column a gap as well (16 px between tick and name); (4) the flyout was the link's own, so it opened at the link's bottom edge over the sentence's second line; (5) `Button.link /template/ ContentPresenter > TextBlock` never matched (`>` is the logical child, and a content TextBlock's logical parent is the Button), so no link was underlined. The screenshot's sizes are about 1.5 x the layout sizes (16 px box -> 24 px), so the display is probably at 150 % scaling.
Fixed once in the shared pieces: every `ListBox.x ListBoxItem` selector is now `ListBox.x > ListBoxItem` (direct items only); `Button.link > TextBlock` underlines only a link's own words; the menu sizes are named resources in `App.axaml` (`MenuPadding` 6, `MenuRowHeight` 26, `MenuRowPadding` 8,0, `MenuRowCornerRadius` 4, `MenuTextInset` 8,2, `MenuGap` 8, `MenuGlyphSize` 12, `MenuSmallFontSize` 10.5, `TickBoxSize` 14 with a 9 px check, `ChipHeight` 22, chip x 8 px in a 14 px slot, `ListMenuMinWidth`/`MaxWidth` 260/300); the size styles sit in a "MENUS" block after `Path.glyph`. New master classes `Button.menurow` (Combine devices..., Switch to ...) and `TextBlock.menutext` (the title and the note); `Button.chipx` is a quiet neutral pill (no accent edge). `FlyoutPresenter.listmenu` sets the base font size and normal text colour (it inherited the sentence's 11 px). OptionList: one lit row (the highlight follows the pointer, `OptionListModel.HighlightEntry`; the accent bar of `.active` is gone, hover and highlight share one wash), tick and icon in one StackPanel. The pop-down is now `FlyoutBase.AttachedFlyout` on the row's name-and-sentence block, opened by the link's Click, so it opens below the whole sentence (FlipY above when no room). Per the owner, the pop-down is mouse first; Esc still closes it and the link stays keyboard reachable.
**Unverified (nothing run or seen):** all sizes on screen, both themes, clicking the link while the pop-down is open (light dismiss may close and reopen it), the Combine devices window's rows (same OptionList, now with the quiet highlight), the side list, page cards, FX list and picker after the `>` selector change (they should look the same: their items are direct children of their lists).

## Volume page: Mixer and Wiring tabs (13 Nov) - plan, written before coding (as-built notes in the next section)

**Decisions (from the repo and the brief; none needed the owner).**
- **One routing model, read by both tabs.** Nothing new is stored. Where a row plays or records from is what it always was:
  `MixerService.PlaysOnOf` (programs), `MasterTargets()` (the master: its several devices in Full control, else Windows' default
  device), `CombinedDevice.Members`. One page function, `TargetsOf(row)`, reads it; the Mixer row's read-only words ("→ Speakers +
  Interface 2") and the Wiring row's chips are both made from it on every sync, so the two can never disagree. Changes go through the
  same `ChooseMasterTarget` / `ChooseProgramTarget` as before (now taking an id instead of an `OptionEntry`) and apply at once.
- **Mixer tab** = the rows as today minus the "plays on" pop-down: the sentence starts with "→" (outputs) or "←" (combined
  microphones) and one `Button.link` with the device names; it jumps to Wiring with that row revealed (focused, so it shows the
  accent focus look, and scrolled into view through the shared `GroupedCards` scroll). A plain microphone has no wiring words (it
  records from itself). The header line (Sound mode button, + Combine devices) leaves the Mixer; a small `Button.link.small`
  "Fast mode" / "Full control mode" sits at the right of the tab strip on the Mixer tab only and jumps to Wiring.
- **Wiring tab**: a top bar (WrapPanel.toolbar) with the existing `SoundModeButton` (its pop-down is unchanged: the two mode cards
  with status, install and fix actions and details; a pop-down keeps the top bar one line high and needs no second copy of the cards),
  the cable chip (`Border.tag` from `SoundModes.Full`'s own first status line: "Ready · 2 virtual cables" / "Needs VB-CABLE", with
  "Install VB-CABLE" beside it when none is installed) and "+ Combine devices" (the existing Combine devices window: it already owns
  name, kind, members and the cable check; Edit reuses it, so there is one combine path). Below: groups "Outputs" (Source | Plays on |
  Cable: master and programs, remembered programs dimmed with their ×), "Microphones" (Source | Records from | Cable: recording
  devices; "This microphone", the cable a processed microphone plays into) and "Combined devices" (one card each: name = members,
  cable state, Edit, ×; the mode's note and Switch link when Fast mode can't run them).
- **Plays on cell**: chips (`Button.chipx`, x = take away; the master's last device has no x) and a dashed "+ add" chip (a new
  chip-sized variant of the dashed master `Button.blockadd`: `.small`). Single-device sources (the master in Fast, a program whose
  sound can't be recorded in Fast) show one `Button.scope` choice with a chevron instead. Both open one small menu
  (`FlyoutPresenter.listmenu`, `Button.menurow` rows, `Border.tag` for "Full control" / "Cable", greyed entries with the reason from
  `SoundModes.Check`, then `Gate.Note` and the one Switch link `ModeGate.SwitchLabel`). Mouse first (AGENTS.md rule 5): rows are
  Up/Down stops, Left/Right walk the cell's button and ×, Enter opens the menu; nothing inside the menu needs keys (Esc closes).
- **Cable column**: the cable a run really uses now (new `EffectsEngine.CableOf(key)`; a combined device's remembered cable in Full
  control), or the engine's own "Needs VB-CABLE" / "Needs another virtual cable" as a caution tag (`SoundModes.IsCableMissing`), else
  "—". The combined cards add the install or "More cables" step (one helper, also used by the mode card and the combine window).
- **Tabs are a shared piece**: `Pages/PageTabs.cs` (model: names, selected, Step) + `Pages/PageTabsView.axaml(.cs)` (the strip:
  `Button.tab`, Left/Right between tabs, `PageTabsView.Attach(page)` = Ctrl+Tab / Ctrl+Shift+Tab, focus follows the selected tab when
  the focused control was hidden by the switch). Only the selected tab takes focus, so it is the one Up/Down stop (`GroupedCards`
  skips header tools that cannot take focus). Master style `Button.tab` in Theme.axaml: muted text, accent text and a 2 px accent
  underline when selected, hover from the master button hover. The tab choice lives in the page view-model (kept for the run).
- **Search**: Sound mode and Combine devices entries get the group "Wiring" ("Volume - Wiring"); mixer rows "Volume - Mixer"; each
  Wiring row ("Plays on" / "Records from") and each combined card is its own entry (editable in the results). No new hotkey: a
  "show tab" action would act on a page that may not be on screen.
- **Cleanup**: the row pop-down (VolumeRowView flyout, its code-behind, `RouteChoices`, `RouteOpening`, `RouteGate`, `SeveralGate`,
  `CombineRequested`, the route entries and `ChooseMember`) goes. `Controls/OptionList` stays for the Combine devices window only, so
  what only the pop-down used goes with it: chips, tags, search box, group headings, icons, disabled entries, `SearchAbove`,
  `OptionChipsMaxHeight`. `MixerService.SetCombinedMembers` becomes `EditCombined(key, name, members)` (Edit). Theme.axaml is 804
  lines: the menu and chip styles move to a new `Styles/Menus.axaml` (included last; the later style still wins as before).

**File plan.** New: `Pages/PageTabs.cs`, `Pages/PageTabsView.axaml(.cs)`, `Pages/Volume/WiringRow.cs` (row, chip, add chip, single
choice, note, menu choice, cable state), `Pages/Volume/WiringRowView.axaml(.cs)`, `Pages/Volume/CombinedCard.cs`,
`Pages/Volume/CombinedCardView.axaml(.cs)`, `Pages/Volume/VolumePageViewModel.Wiring.cs` (groups, rows, cards, menu, cable states,
reveal), `Styles/Menus.axaml`. Changed: `VolumeRow`, `MixerItem`, `InputItem`, `VolumeRowView`, `VolumePage`, the page view-model
partials, `CombineDeviceViewModel` / `CombineWindow` (edit mode), `MixerService.Routing`, `EffectsEngine` (`CableOf`), `SoundModes`
(`IsCableMissing`), `GroupedCards` (`Reveal`, focusable stops), `OptionList(+Model)`, `Theme.axaml`, `Blocks.axaml`, `App.axaml`.

## Volume page: Mixer and Wiring tabs (13 Nov) - as built; compiled only (throwaway builds, 0 warnings, 0 errors); nothing run, seen or clicked

Built to the plan above. File map in `ARCHITECTURE.md` ("Volume page", "Page tabs", "Option list", "Styles"). Differences and things
to know:
- **Single source of truth.** Nothing new is stored; `TargetsOf` / `RouteTextOf` (`VolumePageViewModel.Routing.cs`) are the only
  readers of where a row plays, used by the Mixer sentence and every Wiring cell on every sync (and after every choice:
  `AfterWiringChange` re-syncs the Wiring row and its Mixer row at once). The device words now join with " + " (was ", ").
- **Mixer.** Sentence: "Not open · → Master · no effects", "→ Speakers + Interface 2 · 2 effects · <status>", combined microphones
  "← Mic 1 + Mic 2"; combined speakers no longer end in a stray " · " (`ShowsRouteSeparator`). The device link's tooltip: "Change this
  in the Wiring tab". The old route Switch link on combined rows is gone (their effects link keeps its own Switch). Mode words "Fast
  mode" / "Full control mode" (`Button.link.small`, an Up/Down stop) at the right of the tab strip, Mixer tab only. Meters are fed
  only on the Mixer tab.
- **Wiring.** Top bar: the unchanged `SoundModeButton` and panel (decided: a pop-down, not inline cards, so the bar stays one line and
  the cards exist once), the cable chip (Full control's own first status line, green "Ready · N virtual cables" or yellow "Needs
  VB-CABLE", tooltip = its detail) with "Install VB-CABLE" beside it while none is installed, "+ Combine devices" (the existing
  window, decided: it already owns name, kind, members and the cable check; Edit reuses it, new edit mode: heading "Edit combined
  device", kind fixed, "Save", unplugged members stay members; `MixerService.EditCombined`). Tables share their columns through
  `Grid.IsSharedSizeScope` on the page (Source max 150 px then "...", Cable max 120 px then it wraps). Plain microphones read "This
  microphone" (they record from themselves) and show the cable a processed microphone plays into. A program with an empty list shows
  "Follows the master" before "+ add"; its menu then starts with "Master (follows the default device)" once it has devices. The
  master's last chip has no x (it always plays somewhere). Single-device cells (master in Fast; a program in Fast when recording a
  program fails here) show a `Button.scope` with a chevron; their menu lists the other devices and ends with the gate's note and one
  Switch. Menu tags: the mode's name on a dimmed entry ("Full control"), "Combined" and "Cable" on enabled ones. Cable column: the
  cable a run uses now (`EffectsEngine.CableOf`), a combined device's remembered cable in Full control, the engine's own "Needs
  VB-CABLE" / "Needs another virtual cable" as a caution tag, else "—"; programs that follow the master show "—" (their sound is in the
  master's cable, shown on the master's row). Combined cards: "= members", the same cable cell, "Install VB-CABLE" / "More cables"
  when the engine waits for a cable, Edit, × (asks first, as before). In Fast the section shows "Needs Full control mode" + "Switch to
  Full control"; with none, one hint line.
- **Jump.** A Mixer link opens Wiring, opens the target's group if it was closed, and moves the keyboard to the row or card through
  `GroupedCards.Reveal` (the focused row shows the accent focus look; the page scrolls it into view). From search results the tab
  switches but nothing is revealed (the page is hidden while searching); clear the search to see it.
- **Tabs (shared).** `Pages/PageTabs` + `PageTabsView` + `Button.tab` (its words' colours are set on its presenter, where Fluent's
  button theme colours hover and press). Only the open tab is focusable, so it is the strip's one Up/Down stop; Left/Right on it
  switch (wrapping); Ctrl+Tab / Ctrl+Shift+Tab anywhere on the page switch and put the keyboard on the tab; a click that hides the
  focused control puts the keyboard on the tab without the keyboard look. The choice is kept for the run (not saved). `GroupedCards`
  now also skips header tools that are disabled or not focusable (a dimmed "+ Combine devices" used to be a stop that could not take
  focus; this applies to every page's header tools).
- **Keyboard (rule 5 exception, owner can overrule):** the cell menu is mouse first: rows are Up/Down stops, Left/Right reach "+ add" /
  the one-device choice and ×, Enter opens the menu, Esc closes it (focus back on the button when it was opened from the keyboard);
  the menu's entries are not keyboard-navigable, so adding a device needs the mouse. Chips are mouse only (as before).
- **Search.** "Sound mode" and "Combine devices" now read "Volume - Wiring"; Mixer rows "Volume - Mixer"; new entries: each Wiring row
  (title "Plays on" / "Records from", the source's name as label) and each combined card ("Combined device"), editable in the results
  (their × is hidden there, as on Mixer rows). No hotkey was added (a "show tab" action would act on a page that may not be shown).
- **Removed:** the row pop-down (VolumeRowView flyout and code-behind, `RouteChoices`, `RouteOpening`, `OpenRoute`, `RouteGate`,
  `SeveralGate`, `CombineRequested`, `HasCombine`, the `CombineDevices` command, `RouteLabel`), the route option entries and
  `ChooseMember`, `MixerService.SetCombinedMembers`; from `Controls/OptionList`: chips, tags, search box, group headings, icons,
  disabled entries, one-choice marks, `SearchAbove`, `OptionHeading` (only the pop-down used them); `Border.tickbox.single`,
  `TickBoxRoundRadius`, `OptionChipsMaxHeight`. The page header line is gone from the Mixer.
- **Added shared pieces:** `Styles/Menus.axaml` (moved from Theme.axaml: menus, pop-downs, chips, ticks, `Border.hrule`, the device
  picker menu; Theme.axaml 804 -> about 690 lines; the option cards stayed in Theme so their order against the general hover is
  unchanged), `Button.blockadd.small` (its dashed outline now follows the button's CornerRadius, so the rule editor's blocks keep 6),
  `Button.menurow` content is now stretched (a tag can sit at the right; existing rows look the same), `Pages/Volume/RowIcon`,
  `SourceName`, `CableView`, `WiringTable` (one copy each instead of repeated XAML), `CombineDeviceViewModel` edit mode, the cable steps
  `CableSetupAction` / `MoreCablesAction` (one each; "More cables" was built twice before), `ChevronSize`, `RowPictureSize`,
  `MenuOffset`, `Tab*`, `Wiring*`, `ChipAddPadding`, `ChipSpacing` in `App.axaml`.

**Not verified (nothing was run).** Everything on screen: the tab strip and its underline in both themes, hover and focus looks
(the generic button hover on a tab), the tables at the narrowest window (720 px: about 430 px of page, so chips wrap and the Source
names trim), the chip and dashed-chip sizes side by side, the cell menu's placement and width, the combined cards' tool column, the
search results' Wiring rows; behaviour: the jump's reveal and scroll after the tab switch (containers of the hidden tab are realised
on the first layout after it shows; if `Reveal` finds none it does nothing), Ctrl+Tab, focus after a choice, Esc in the menu, the edit
window. No device, default device or cable was changed.

**Open items.**
- Keyboard users can open a cell's menu but not pick in it (by the rule 5 exception); if the owner wants it, the menu's rows could take
  Up/Down/Enter like `OptionPanelKeys`.
- A program that follows the master shows "—" in the Cable column even when the master bus carries it through a cable.
- From search results a device link switches the tab but cannot leave the search; the shell has no "go to" for a result.

**Owner test steps.**
1. Volume page: tabs "Mixer" and "Wiring" under the title; Mixer shows the rows without the old header line; rows read "→ Speakers ·
   1 effect ..."; "Fast mode" at the right of the tabs opens Wiring.
2. Click a program's device words: Wiring opens with that row lit and in view. Same for the master, a combined device (its card) and a
   combined microphone.
3. Wiring, Fast mode: the master shows one choice with a chevron; its menu lists the other devices, combined speakers dimmed with
   "Full control", then "Fast mode plays on one device." and "Switch to Full control". Pick a device: Windows' default changes
   (Settings > Sound) and the Mixer's master words follow at once.
4. A program: "Follows the master" and "+ add"; add Headphones: a chip appears, the Mixer says "→ Headphones"; click the chip: it goes,
   "Follows the master" returns.
5. Full control: the master gets chips and "+ add" (its only chip has no x); add a second device; the Cable column shows the cable or
   "Needs another virtual cable".
6. "+ Combine devices" creates a device (card "= Interface 1 + Interface 2"); Edit renames it and changes its members; × asks first.
7. Keyboard: Up/Down from the tab through the top bar, tables and cards; Left/Right on the tab switches; Ctrl+Tab anywhere; Enter on
   "+ add" opens the menu, Esc closes it.
8. Both themes, the narrowest window, search "sound mode", "combine", a program's name (Mixer and Wiring results).

## Microphone effects in Fast mode, through Equalizer APO (14 Nov) - researched before coding (written up with the build); compiled only (throwaway build, 0 errors); nothing run, written to Equalizer APO's folder, recorded or heard

**Research (before coding).** Sources: the fork's source (upstream Equalizer APO 1.4.2, commit bbfcc3e, the same version as the
owner's install) in `native/EqualizerAPO`, the online Configuration reference (sourceforge wiki), read-only registry reads on this PC.
- Confirmed (source): **Equalizer APO runs on recording devices.** `DeviceAPOInfo::load` looks the device GUID up under
  `MMDevices\Audio\Render` and, when it is not there, under `...\Capture` (`input = true`); `FilterEngine::setDeviceInfo(capture, ...)`;
  `StageFilterFactory`: a capture device has only the `capture` stage and it is selected from the start, so no `Stage:` line is needed.
  Device Selector (`DeviceAPOInfo::install`) puts only the **pre-mix** APO on a recording device (`installPostMix = !input`; LFX or SFX
  slot), so a microphone's chain runs once. The Device Selector of 1.4.2 has a Capture devices tab (Device Selector itself is not in the
  fork; that tab is from the earlier redesign agent's check, not re-read here).
- Confirmed (source, `DeviceFilterFactory.cpp`): `Device: <pattern>` compares each space-separated word (lower case) with the device
  string "connection name + device name + {GUID}" (`DeviceAPOInfo::getDeviceString`); a word with `{` is matched against the string
  with the GUID, other words against it without. `;` separates alternatives; `all` always matches. A non-matching Device line makes every
  following command except Device lines be skipped **until the next Device line**; at the **end of a file** (`endOfFile`) it matches
  again, so a Device selection inside an included file never leaks into the including file. There are **no Capture/Playback keywords**
  in 1.4.2's device string (the reference page says the pattern is matched against "the current output device"; the source shows it is
  the current device, playback or recording). An endpoint GUID is unique across playback and recording devices, so `Device: {guid}`
  alone picks exactly one device.
- Consequence for OmniControl: ONE `OmniControl.txt`, included once at the top of `config.txt` (as before), holds one section per
  device: `# Master` / `Device: {default playback guid}` / its lines, then `# Microphone: <name>` / `Device: {mic guid}` / its lines.
  Each device's Equalizer APO instance reads the whole file and runs only its own section; the owner's own `config.txt` after the
  include is processed exactly as before (Device state matches again after our file; Stage and Channel state are restored per file by
  Equalizer APO). No second include, no per-device file.
- Confirmed: Equalizer APO runs the same commands on a microphone as on a playback device (`Preamp`, `Filter` with every type
  OmniControl writes, `Convolution`), so the master writer's rules are reused unchanged: Equaliser (one Filter line per band that changes
  the sound, LSC/HSC/PK/LPQ/HPQ/BP/NO/AP with Q, frequencies with at most two decimals), the auto Preamp headroom (overall gain minus
  the bands' highest boost), Gain (Preamp), IR Loader (Convolution, only at 100 % mix). Not run there (saved only, said on the row):
  VST3 plug-ins (no VST3 host in the APO), any effect below 100 % wet, Compressor/Limiter/Delay/Gate/Reverb (no engine runs them yet).
  Inferred, unverified: an IR file with more channels than a mono microphone is mapped by Equalizer APO's convolution filter itself.
- Confirmed (source): a recording device's registration is read the same way as a playback device's (FxProperties LFX/GFX/SFX/MFX/EFX
  holding Equalizer APO's CLSIDs; the "enhancements off" value `{1da5d803-...},5`), under the Capture key. Implemented once for both
  (`ApoStatus.EndpointOf`; `ApoStatus.EndpointKey` = Equalizer APO's own Render-then-Capture rule, also used by `ApoPlanner`).
- **The owner's own config applies to a microphone too.** Ticking a microphone in Device Selector makes Equalizer APO read the same
  `config.txt` for it. The owner's `config.txt` today is `Preamp: -6 dB` + a `GraphicEQ:` line with no `Device:` line, so once the
  microphone is ticked those also run on it (after OmniControl's section). OmniControl never edits the user's lines; the row's Choose
  devices tooltip says so in one sentence. To keep them on the speakers only, the owner adds a `Device:` line above them (for example
  with the Configuration Editor's device button).
- This PC (read only): one active recording device, "Microphone Array" (AMD Audio Device), `{8d583f4c-56c5-4347-a56d-cc27105df83b}`.
  It has FxProperties with no LFX/GFX/SFX/MFX/EFX value but the composite slots 13/14/15 (Realtek's Intel APOs, several in MFX), the
  same pattern as the Speaker; enhancements are not off. Equalizer APO is not on it yet. Device Selector will choose its SFX/EFX layout
  (composite values present) and write only SFX for a microphone, whose processing modes are already declared as Default + Speech.
- Driver caveats (inferred from Windows' APO rules and Equalizer APO's FAQ; none tested): an app recording in **exclusive mode** (some
  DAWs and ASIO-style tools) bypasses every APO; streams opened in **raw** or **communications** processing mode (some call apps,
  Windows' voice effects) may not pass through an SFX that only lists Default and Speech modes; a driver that uses composite APOs may
  ignore the legacy SFX slot on some Windows builds (Device Selector's "Troubleshooting options" can pick another install mode); some
  vendor drivers reject third-party APOs on capture endpoints; Windows' "Audio enhancements off" on the microphone disables it (shown as
  "Audio enhancements are off on this microphone"). A restart (or at least restarting the recording app) may be needed after Device
  Selector.

**What was built.**
- Core: `ApoEndpoint` (one endpoint's slots and enhancements value), `ApoState.Output` (the default playback device) and
  `ApoState.Microphones` (every recording device except cables, read by `AudioEffectsProbe`); `UpstreamUsable`, `ForkUsable`,
  `FolderFor(endpoint)` (Equalizer APO first, then OmniControl APO; null when neither runs there), `MicrophoneOf(id)`. `ConfigTarget`,
  `UsesUpstream`, `Ready`, `OnDevice`, `UpstreamOnDevice`, `EnhancementsOff` keep their meaning (derived from `Output`). `ApoState`
  compares its microphone list by content (no change event when nothing changed). `ApoIds.CaptureKey`; `ApoPlanner` finds the device key
  by the same rule (the fork's install is still offered for the default playback device only).
- App: `Engine/ApoChainWriter.cs` (was `MasterChainWriter.cs`: `Section(title, chain, deviceGuid)` + `File(sections)`; the same
  translation rules), new `Engine/ApoChains.cs` (Fast mode's chains in the APO: one section per row in the config folder whose APO runs
  on that row's device; files touched only when the text changes; every other folder cleaned; the first call after start-up always
  cleans or writes, which is the crash recovery as before; per-row status). `RoutePlanner`: `MasterToApo` -> `ToApo` (the master key
  and every plugged-in microphone with effects on and something enabled). `EffectsEngine.ApplyApo` builds the rows and sets their
  status; Dispose takes every line out (`ApoChains.Clear`). A microphone's section goes exactly as the master's does: its effects off,
  its last effect disabled, the mode switched to Full control, the microphone unplugged, exit; a crashed run's lines are taken out at
  the next start.
- Status (honest): running = "Chain sent to your Equalizer APO · no extra delay" (the master too: before it said "Using your Equalizer
  APO", which implied more than OmniControl can know), plus "saved only, not run here: <names>" for effects the APO does not run
  (`EngineStatus.SkippedNote`, the wording changed for every engine); missing = "Equalizer APO is not on this microphone. Recording
  without effects." (or "not registered", "Audio enhancements are off on this microphone", "not loaded by Windows", "not installed"),
  from one shared check `SoundModes.EndpointMissing` behind `ApoMissing` (master) and `MicApoMissing` (microphones).
- Sound modes: `SoundMode` has a third method, `Microphone` (Fast "in Windows (Equalizer APO) · no extra delay", Full "through the
  cable"); `MethodFor(key)` picks master / microphone / program from the row key (`EngineStatus.LineFor(mode, key)`, used by the row's
  tooltip and the FX window line); the card's summary groups the rows by method ("Master and microphones: in Windows (Equalizer APO) ·
  no extra delay. Programs: by recording · about 30-80 ms delay."). Matrix: `Check(MicEffects)` in Fast is open when Equalizer APO is
  installed (or an APO already runs on some microphone) and greyed only when it is not ("Fast mode runs microphone effects in Equalizer
  APO, which is not installed" + Switch to Full control). Combined microphones and speakers still need Full control, one reason
  `SoundModes.CombinedNeedsFull` ("Needs Full control mode: a combined device runs through a virtual cable"), also used by the planner
  (its own copy of "Needs Full control mode" removed). Fast's third status line: "Microphones: Equalizer APO on N" / "is on none" /
  "need Equalizer APO" (never yellow). Fast's description and details name microphones and say VST3 is kept but not run there.
- Choose devices: the Fast card offers it whenever Equalizer APO is installed (the main button only when the master can't run; tooltip:
  tick the playback device, and microphones on its Capture tab). A microphone row whose status says Equalizer APO can't run there (and
  Device Selector can fix it) shows a **Choose devices** link after its status (`VolumeRow.Fix`, a `ModeCardAction`, `Button.link.small`;
  tooltip: the Capture tab, a restart may be needed, the user's own settings apply to it too).
- `SoundModeService` looks again when a playback or recording device comes or goes or the default changes (it compares the device list
  on `MixerService.Updated`), so a microphone plugged in later gets its Equalizer APO state (this also fixes the master's state going
  stale after a default-device change made outside OmniControl).
- The include marker is now "# Added by OmniControl for its effects; ..." (`ApoIds.IncludeMarker`); the old "for its master effects"
  marker is still recognised and removed (`ApoIds.PreviousIncludeMarker`).
- Also: `FxSlotViewModel.RunsNote` ("... which runs the master and microphones in Fast mode"), `FxEngines.Apo` doc, the
  `EngineInstaller` download-page sentence, search words (Sound mode: microphone, capture, device selector; Wiring rows: records from,
  microphone), `MixerService.InputIdOf` (one place for "input:" key -> endpoint id).
- Master action list: nothing new. "Toggle microphone FX" (`volume.mic.fx`) already toggles the default microphone's effects, and the
  engine now runs them in Fast mode too (read in code: toggle -> `MixerService.SetFxOn` -> `FxChanged` -> `Reconcile` -> `ToApo`).
  Choose devices is a setup step, not an action.

**Not verified.** Nothing was run. Equalizer APO is not on this PC's microphone; no chain was written; nobody recorded. Not seen: the
row's Choose devices link at the narrowest window (it wraps with the sentence), both themes, the Fast card's third status line.

**Open items.** A combined microphone in Fast could run each member's chain in Equalizer APO, but mixing the members needs a cable:
kept Full control only. The fork's advanced install is not offered for microphones (the planner supports the key, the UI does not).
The owner's own `config.txt` lines apply to a ticked microphone (see research); OmniControl does not offer to limit them.

**Owner test steps.**
1. Volume > Wiring > Sound mode: the Fast card shows "Microphones: Equalizer APO is on none" and Choose devices. Press it, approve the
   administrator prompt, open the **Capture devices** tab, tick "Microphone Array", OK; restart Windows if asked.
2. Mixer > Microphones > the microphone's FX: add an Equaliser, one band +12 dB at 1 kHz (easy to hear), Effects on. The row says
   "Chain sent to your Equalizer APO · no extra delay". `C:\Program Files\EqualizerAPO\config\OmniControl.txt` has
   `# Microphone: ...`, `Device: {8d583f4c-...}`, `Preamp: -12 dB` and the Filter line.
3. Record with Windows Sound Recorder (or Audacity in its default, non-exclusive mode): the EQ is heard. Try a call app too (Discord or
   Teams may use communications mode and bypass it: report which).
4. Add a VST3 to the microphone chain: the status adds "saved only, not run here: <name>". Effects off, Full control, or exit: the
   microphone section (and, with no master effects, the include line and the file) disappear.
5. Before ticking it: with effects on, the row says "Equalizer APO is not on this microphone. Recording without effects." with Choose
   devices.
6. Your own `Preamp: -6 dB` and GraphicEQ also run on the microphone once it is ticked, unless you add a `Device:` line above them.

## FX editors for small screens: master scroll bar, pop out any effect, Parameters list, Scale (14 Nov) - compiled only (throwaway build, 0 warnings, 0 errors); nothing run, seen, heard, or tried with a real plug-in

> Superseded in part on 15 Nov ("FX window tidy, keyboard instructions off screen", the last sections): the keyboard footer strip, the CPU number, the Output picker in the header and the controls under and above the editor are gone; the toolbar row, the status strip and Options > Plays on / Keyboard shortcuts replace them.

Owner: "Very difficult to operate the plug-in e.g. EQ in small window." Four requests, built as below.

**1. One master scroll bar** (`Styles/ScrollBars.axaml`, included last in `App.axaml`). Every `ScrollViewer` and `ScrollBar` in the
app: `AllowAutoHide = False`, so Fluent's own template puts the content beside the bars instead of under them (that overlap is why the
EQ card's rounded accent frame ran under the thin bar); `ScrollBarSize` (Fluent's resource) = 14, so the bar, arrows and thumb follow;
a visible track (`BrushScrollTrack`, both themes in `Brushes.axaml`), a clear thumb (`BrushScrollThumb`, the accent under the pointer
or while dragged) inset 3 px that only changes colour while the pointer is over it (its size never changes, so the bar does not wobble). It applies to the
main page scroll viewer, the FX editor, pop-down lists, menus and text boxes alike (bars only show when something overflows). The plug-in
editor area's own bars now take the master style and read their thickness from it (`EditorFit.Scroll(..., bar)`; `EditorFit.BarThickness`
is gone). Look change everywhere: a scroll bar now takes 14 px beside the content instead of floating over it.

**2. Double-click pops out any effect.** `FxChainViewModel.TogglePopOut` works for every kind. `IPluginEditors` / `PluginEditorDock`
became `IEffectEditors` / `EffectEditorDock`, and `PluginEditorWindow` became `EffectEditorWindow.axaml(.cs)` (owned by the FX window
through `OwnedWindow`, as before): a bar with the effect's name and a Put back pill (double-click the bar does the same; Windows' own
title bar double-click still maximises, that can't be taken over without custom chrome), then the editor: a built-in effect gets the same
`EffectEditorView` as the FX window (starting at `EditorFit.OwnEditorWidth/Height` times its scale, resizable); a plug-in in Parameters
gets its settings list there; a plug-in's own editor gets the `PluginEditorArea` as before. Closing the window puts the effect back. The
pane then says "This effect's window is open on its own. Put back shows it here." Footer: "Double-click own window, again back".
Enter on a row still gives the editor the keyboard. The EQ's preset drop-down stays in the FX window's toolbar (not in the pop-out).

**3. Parameters.** A `Parameters` pill (`Button.pill`, `.custom` while on) in a new editor header line above the editor, and Options in
the FX menu ("Parameters (plain list)"). Default off; remembered per effect type (decision: `AppSettings.EffectEditors`, keyed by the
built-in kind or the plug-in's file, so every copy of TDR Nova looks the same on a small screen; stored in settings.json, not mixer.json,
so a view change never rebuilds the engine's chain). Greyed with a tooltip for built-ins that are already a list of sliders (Gain,
Compressor, IR Loader...).
- Equaliser: one card per band (`EqBandRow.axaml`, the selected-band block moved out of `EqPanelRow`, now shared by both: On, Type,
  Frequency with note, Gain, Bandwidth with Q; shared slider, wheel, typing and double-click reset as before), then the Overall gain card.
  The list follows Add/Remove/Reset/preset (it watches the panel's bands). Add/Remove band stay in the graph view.
- VST3: `Vst3Effect.Params.cs` (Core) lists the settings from NAudio.Vst3 (`Vst3Plugin.Parameters`, `Units`), leaves out read-only,
  hidden and the bypass (OmniControl's On switch does that), groups them by unit name path ("Band 1", "Filter / Envelope").
  **How changes reach the sound** (checked in NAudio.Vst3 3.3.0's source, tag v3.3.0): `Vst3Parameter.NormalizedValue = x` calls
  `IEditController::setParamNormalized` at once AND queues the change, which `Process` hands to the processor in the next block through
  `IParameterChanges` at offset 0 - the correct VST3 host path, so no host change was needed for a running chain. Gap fixed in our host
  code: while no chain runs the plug-in, no block is processed, so a split-component plug-in's processor would never see the change and
  its saved state (`IComponent::getState`) would lose it. `Vst3Effect.SaveState` now first runs one silent frame (`FlushIfIdle`) when
  the audio thread has not run the plug-in for `CoreTiming.PluginIdleMs` (500 ms), under the same lock the audio thread's `Process` now
  takes (`_processGate`; uncontended except during a flush). A 0-frame flush was not used: NAudio 3.3.0 would throw in
  `DispatchPendingParamChanges` (clamp to -1) when an editor edit is pending.
  App: `Vst3ChainHost.OpenParameters` (held like an open editor, closed with `EditorClosed`, which saves the state);
  `PluginParameterList` / `PluginParamGroup` / `PluginParamItem` (slider over the steps: `StepCount` for stepped settings, 1000 for
  continuous; the plug-in's own value text (`getParamStringByValue`) plus its units; double-click the value to type it in the setting's
  own units (`SetPlainValue`, the plug-in holds it in range); double-click the slider = the plug-in's default; after each change every
  setting is read again, since plug-ins move linked settings); search box above the list when there are more than 12 settings;
  `PluginParamGroupRow.axaml`. A plug-in whose list has nothing a host may change says "This plug-in has no settings a host can change."
  The plug-in's own editor is closed while its list shows (they never fight over values; reopening the editor reads the new values).
  Not available from NAudio 3.3.0 without private access: the plain range of a setting (`normalizedParamToPlain` of 0 and 1), so the typed
  box takes any number and the plug-in clamps it; `getParamValueByString` (typing the plug-in's own text form).

**4. Scale.** `EditorScale.cs` = the one list (60, 70, 80, 90, 100, 125, 150 %, default 100); the header's "Scale" drop-down
(`ComboBox.tool`, wheel steps it), FX menu "Smaller editor" / "Larger editor" / "Editor at 100 %" and Ctrl+- / Ctrl+= / Ctrl+0 from
either pane. Remembered per effect type with Parameters (same reasoning: the screen is the user's, but each editor's own size differs).
- OmniControl's editors (EQ graph view, its band list, sliders, a plug-in's settings list): `EffectEditorView` puts the cards in a
  `LayoutTransformControl` (a layout scale: text, hit-testing, wrapping follow) inside one scroll viewer outside the scale, so the bar keeps
  its size; the list is not virtualised (a plain `StackPanel`), so the outer viewer scrolls and keyboard moves still bring cards into view.
- A plug-in's own window: `Vst3Editor.TryScale(display scale x user scale)` = `IPlugViewContentScaleSupport::setContentScaleFactor`
  (NAudio's `SetContentScaleFactor`). NAudio hides whether the view implements the interface (its QueryInterface result is private), so
  support is judged by what a host can see: the editor changes its size (asks to resize, or reports a new size) during the call. If not,
  the scale is put back, the effect's Scale returns to 100 % and is greyed with the tooltip "This plug-in did not change its window's size
  when asked, so OmniControl can't scale it..." (`PluginEditorArea.ScaleRefused` -> `FxSlotViewModel.PluginScaleRefused`, for this window's
  session). So the drop-down is enabled until a plug-in is seen to refuse; nothing is faked. When it takes, the FX window's grow maths and
  a pop-out's fit use the editor's new pixel size (the FX window still only grows, never shrinks).

**Files.** New: `Styles/ScrollBars.axaml`; `Fx/EditorScale.cs`, `EffectEditorView.axaml(.cs)`, `EffectEditorWindow.axaml(.cs)`,
`EqBandRow.axaml(.cs)`, `FxSlotViewModel.View.cs`, `PluginParameterList.cs`, `PluginParamItem.cs`, `PluginParamGroupRow.axaml(.cs)`;
renamed/rewritten: `IEffectEditors.cs`, `EffectEditorDock.cs`; removed: `PluginEditorWindow.cs`; Core: `Vst3Effect.Params.cs`,
`PluginCall.cs` (the one plug-in call guard, used by the editor and the settings). Changed: `FxChainPanel.axaml(.cs)` (header, view,
keys), `FxChainViewModel.cs`, `FxSlotViewModel.cs`, `FxWindow.axaml` (menu, footer), `FxWindowService.cs`, `PluginEditorArea.cs`,
`EditorFit.cs`, `EqPanelRow.axaml`, `Vst3Effect.cs`, `Vst3ChainHost.cs`, `AppSettings.cs` (`EffectEditors`, `EffectEditorPref`),
`CoreTiming.cs`, `Brushes.axaml`, `App.axaml` (templates, `ParamLabelMaxWidth`, `ParamValueMaxWidth`), `Theme.axaml` (comment),
`AGENTS.md` (master scroll bar, `.custom` toggle).

**Not verified.** Nothing was run or seen: the scroll bar look in both themes and at the main window's narrowest width (content is now
14 px narrower where a page scrolls), the header line at the narrowest FX window (560: Parameters + Scale should fit beside each other
in the ~280 px pane, else it wraps), the scaled EQ (layout scale with the graph's drag maths, which uses its own coordinates and should
follow), the pop-out windows' sizes, keyboard in the settings list. No real plug-in was loaded: the parameter list, its grouping, value
texts, typing in plain units, the silent-frame flush and saving, and the content-scale test (JUCE plug-ins on Windows often scale by
themselves from the display; some may answer the call asynchronously, which this check would read as a refusal) are all unverified.

**Open items.** Add/Remove band are not in the equaliser's settings list (use the graph view). The settings list re-reads every setting
after each change (fine for hundreds; a plug-in with thousands may feel slow while dragging). A plug-in that changes settings on its own
(automation from its editor, restartComponent) is not followed while its list is open. The scale refusal is remembered per FX window
session only. The EQ preset choice is not in a popped-out EQ window. Windows' own title bar double-click maximises the pop-out.

**Owner test steps.**
1. Main window and FX window, both themes: a page or editor that scrolls shows a 14 px bar with a visible track and a grey thumb that
   turns orange under the pointer without changing size; the EQ card's accent frame is no longer under the bar.
2. FX window of a microphone with an Equaliser, made small: Scale 70 % (or Ctrl+-): the whole editor shrinks, text included; the bar
   stays its size; dragging points still hits them. Reopen the window: still 70 %. Ctrl+0: back to 100 %.
3. Parameters on the Equaliser: one card per band with On, Type, Frequency, Gain, Bandwidth/Q, then Overall gain; wheel, typing
   (double-click the value) and double-click reset work; press Parameters again: the graph. Change a preset meanwhile: the list follows.
4. A VST3 with an editor (TDR Nova): Parameters shows its settings grouped, with a search box; move a slider while music plays through it
   (Full control): the sound changes; press Parameters again: its own editor shows the new value. Close and reopen OmniControl: kept.
5. A VST3 without an editor (Safety Limiter, Loudness War Winner): Parameters shows its settings; change one with the engine stopped,
   close the FX window, reopen: kept.
6. Scale on a VST3 editor (TDR Nova, Surge XT Effects): 70 % either shrinks the plug-in's window (the FX window grows only if needed) or
   the drop-down returns to 100 % greyed out with the tooltip.
7. Double-click the Equaliser row: it opens in its own window with Put back; double-click the bar or press Put back: it returns.
   Same for a VST3 (its own editor) and for a VST3 in Parameters (its list). Toggle Parameters while a VST3 is popped out: its window is
   made again in the new form.

## Three equalisers: Parametric, 32 Bands, 16 Bands (14 Nov) - plan, written before coding (as-built notes in the next section; the plan held, differences listed there)

Owner-approved job: the ReaEQ-style equaliser renamed "Equaliser - Parametric", plus two graphic equalisers "Equaliser - 32 Bands"
and "Equaliser - 16 Bands" on the same band model and engine path.

**Names and ids.** Kinds (stored in mixer.json as `FxSlot.Kind`, never renamed): `eq` = "Equaliser - Parametric" (the existing
one, id unchanged), `geq32` = "Equaliser - 32 Bands", `geq16` = "Equaliser - 16 Bands". The catalog entry (`FxCatalog.All`) is the
one source of a built-in's name: on load every built-in slot takes its name from its kind (`MixerSettings.Load`), so an old slot
named "Equaliser" reads "Equaliser - Parametric" and a later rename needs no migration. Editor preferences (`AppSettings.EffectEditors`)
stay keyed by kind ("eq" keeps its Scale and Parameters choice).

**Band centres (defined once, `Audio/GraphicEq.cs`).** The ISO 266 preferred third-octave centres from 16 Hz to 20 kHz: 16, 20, 25,
31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1k, 1.25k, 1.6k, 2k, 2.5k, 3.15k, 4k, 5k, 6.3k, 8k, 10k,
12.5k, 16k, 20k = 32. Decision on the 32nd: 20 Hz to 20 kHz is exactly 10 octaves = 31 third-octave centres; the only standard centre
that keeps the spacing even and stays playable at 44.1/48 kHz is 16 Hz (the next one up, 25 kHz, is above what 48 kHz can carry). The
16-band set is every second entry of the same list from 20 Hz: 20, 31.5, 50, 80, 125, 200, 315, 500, 800, 1.25k, 2k, 3.15k, 5k, 8k,
12.5k, 20k (standard centres, two-thirds of an octave apart, exactly 20 Hz to 20 kHz; the owner's 25/40/63... example has 15 and
needs a 16th outside the range or off the spacing).

**Q formula (once, `GraphicBands.Q`).** Each band is a peaking (`EqType.Peak`) band as wide as the spacing: bandwidth = octaves
between centres (1/3 or 2/3), Q = `EqMath.QOf(bandwidth)` (the cookbook relation already used by the Parametric): 4.32 and 2.15.
Neighbouring bands meet at their half-gain points, as on a constant-Q graphic EQ; raised neighbours add up (the curve shows the true
total).

**Engine path (no second engine).** A graphic EQ's slot holds ordinary `EqBand`s (id = place 1..N, Peak, fixed centre and Q, gain
from the slider), so `EqStage` (glide, identity at 0 dB, flushing), the limiter, `ApoChainWriter` (Filter: ON PK lines, automatic
Preamp headroom), the Parameters view and Scale all work unchanged. What makes a kind an equaliser is a field on its catalog entry,
`FxKind.Eq` (an `EqLayout`: default bands, presets, loading older bands, and for a graphic EQ its fixed `GraphicBands`); the engine
and the APO writer ask `FxCatalog.EqLayoutOf(slot)` instead of comparing with "eq". Honest status unchanged: all three are
`FxEngines.Program | FxEngines.Apo`.

**Migration.** Parametric: `Eq.Upgrade` as before. Graphic: `GraphicBands.Upgrade` rebuilds the bands from the fixed definition and
keeps each band's gain by id, so frequencies and Q always follow the definition. Slot names from the catalog (above).

**Presets (once).** The preset shapes are one table in `Eq` (gains at the classic 31 Hz-16 kHz points: Bass boost, Treble boost,
Vocal, V shape, Loudness, Night); the Parametric's presets are those bands as before (same sound), a graphic EQ samples each shape at its
own centres (log interpolation, ends held, 0.5 dB steps). Flat = every band 0 dB. Preset choice, matching and "Custom" move to the
shared `EqLayout`, so the toolbar's preset drop-down serves all three.

**Shared response helper (`EqMath`, for the later whole-chain curve).** `LogGrid(points, min, max)` = frequencies evenly spread on a
log scale; `CurveDb(bands, gainDb, freqs)` = the total response in dB of the bands plus a gain at each frequency (coefficients worked
out once per band). `PeakDb` and the Parametric graph use them; a chain's total is the sum of each equaliser slot's `CurveDb`.

**Editor (graphic).** `Fx/GraphicEqItem.cs` (card: bands, range, overall gain for the curve, Flat, edits), `GraphicBandItem.cs` (one
band: gain, slider position, labels), `GraphicEqRow.axaml(.cs)` (curve above, dB scale left, one vertical slider per band, short labels
under them, Flat and Range under that; scrolls sideways when narrow), `GraphicEqCurve.cs` / `GainScale.cs` / `BandLabels.cs` (drawn:
the total response, the dB scale, the labels thinned to every other one when they would touch), `SliderPaint.cs` (press on a slider
and drag across the others to draw a curve), `GraphicBandRow.axaml(.cs)` (one band as a card of the Parameters list: slider and
typed dB). Master style `Slider.vband` (+ `.large` for the 16-band set) in `Theme.axaml`. Keyboard: the vertical-slider rule goes
into the shared `RowEditing` once (Up/Down step, Shift or PageUp/PageDown large, Ctrl fine, Home or 0 = the slider's reset value,
Esc = back to the card; Left/Right walk the parts as on every card). Range ±12 / ±24 dB is a view choice per kind
(`EffectEditorPref.WideRange`, default ±12): switching never changes a gain. The two editors share `Fx/IEqEditor.cs` (load, export,
edited, overall gain, band cards), so `FxSlotViewModel` builds the preset choice and the Parameters list once for all three.

## Three equalisers: Parametric, 32 Bands, 16 Bands (14 Nov) - as built; compiled only (throwaway builds, 0 warnings, 0 errors); nothing run, seen, heard or clicked

**What it is now.** The Add effect list (it reads `FxCatalog.All`) shows "Equaliser - Parametric" (the ReaEQ-style one, id `eq`),
"Equaliser - 32 Bands" (`geq32`) and "Equaliser - 16 Bands" (`geq16`), one after another after IR Loader. All three run in both
engines and in the APO (`FxEngines.Program | FxEngines.Apo`), so the "Runs in both sound modes" note is unchanged.
- Model: `FxKind.Eq` (an `EqLayout`) marks a kind as an equaliser; `ChainBuilder`, `ApoChainWriter`, `FxSlotViewModel` and
  `MixerSettings` ask `FxCatalog.EqLayoutOf(slot)`, nothing compares with "eq" any more except the legacy single-EQ conversion (it
  makes a Parametric). A graphic EQ's slot holds 32 or 16 ordinary `EqBand`s (id 1..N, Peak, fixed centre, Q 4.32 or 2.15, gain), so
  `EqStage`, the limiter, the APO's `Filter: ON PK` lines and its automatic Preamp, Parameters and Scale work unchanged; no DSP was added.
- Names: `MixerSettings.UpgradeBuiltIns` gives every built-in slot its catalog name on load (an old "Equaliser" reads "Equaliser -
  Parametric", in the chain list, the APO file's comments and the engine's "skipped" lists); bands are loaded by the kind's
  `EqLayout.Upgrade` (Parametric: `Eq.Upgrade` as before; graphic: rebuilt from the definition, gains kept by id). Stored ids and the
  per-kind editor preferences (`EffectEditors["eq"]`) are unchanged; no migration file step was needed.
- Presets: one table, `Eq.PresetShapes` (Bass boost, Treble boost, Vocal, V shape, Loudness, Night as gains at 31 Hz-16 kHz). The
  Parametric's presets are those 10 bands as before (same sound, still recognised); a graphic EQ samples each shape at its centres
  (`Eq.ShapeAt`: log-straight between the points, ends held, rounded to 0.5 dB). Flat = every band at 0 dB. The toolbar's Preset
  drop-down and "Custom" come from `EqLayout` for all three.
- Graphic editor: response curve (the bands plus overall gain, `EqMath.CurveDb`, each centre above its slider, the range's halves as
  faint lines, the band last changed or focused read out top left), dB scale left, 32 or 16 vertical sliders (`Slider.vband`; the 16-band
  set `.large`), short labels under them (every second, or fourth, one when they would touch; the band last changed in the accent), then
  a Flat pill and a Range switch (±12 dB / ±24 dB, default ±12; per kind in settings.json; switching changes only how far the sliders
  reach, never a gain: a gain beyond the range shows at the slider's end with its true value in the readout). The overall gain is the
  ordinary slider card after it, as on the Parametric.
- Keys (shared rule for every vertical slider, `RowEditing`): Left/Right move between the bands (then on to Flat and Range); Up/Down a
  step (0.5 dB at ±12, 1 dB at ±24), Shift or PageUp/PageDown 3 dB (6), Ctrl 0.1 dB (0.2); Home or 0 = 0 dB; Esc gives the keyboard to
  the card (the FX window does not close on that Esc; a second Esc closes it as before), from where Up/Down move between cards. Enter
  does nothing on a vertical slider (no "adjust" mode). Mouse: drag a knob or click the track; double-click = 0 dB (`SliderReset`); the
  wheel moves 1/100 of the slider's span (the shared rule: 0.24 dB at ±12); press on one slider and drag sideways = draw a curve
  (`SliderPaint`: every slider passed is set to the pointer's height; a fast sweep fills the skipped ones in a straight line).
  Typing: the Parameters view lists one card per band (frequency, slider, the gain as text: double-click or Ctrl+Enter, -24..+24 dB
  whatever the range) and then the overall gain.
- Narrow window: the sliders' least width is 16 px per band (28 for the 16-band set), so the 32-band editor needs about 540 px inside
  the editor pane and scrolls sideways with the master scroll bar below the labels (curve, sliders and labels together; the dB scale
  stays put). Scale (60-150 %) scales all of it.

**Shared response helper (for the next job: the whole chain's curve).** In `Audio/EqMath.cs`, no UI:
- `double[] EqMath.LogGrid(int points, double min, double max)` - frequencies spread evenly on a log scale (at least 2 points).
- `double[] EqMath.CurveDb(IEnumerable<EqBand> bands, double gainDb, IReadOnlyList<double> freqs)` - the total magnitude response in dB
  of the bands plus a gain, at each frequency; coefficients are worked out once per call (flat bands skipped), at the graph rate
  (`EqMath.GraphSampleRate`, 48 kHz; keep frequencies below `GraphSampleRate * EqMath.MaxFreqShare`).
- For one slot: `FxCatalog.EqLayoutOf(slot)` is not null -> `CurveDb(slot.Bands ?? layout.Default(), FxCatalog.EqGainOf(slot), freqs)`.
  A chain's total = the sum, frequency by frequency, of each enabled equaliser slot's curve (plus a Gain slot's dB); a Wet below 100 %
  mixes the dry sound back: `CurveDb(..., wet)` does that (14 Nov, the FX window header; the whole chain is `Audio/ChainResponse.cs`). `PeakDb`, the Parametric's
  graph and the graphic curve already use it. `TotalDb` and `ResponseDb` were removed (replaced by it).

**Differences from the plan.** The slider styles (the global tick rule, `Slider.compact`, the new `Slider.vband`) moved out of
`Theme.axaml` into `Styles/Sliders.axaml` (Theme.axaml would have passed 700 lines; their focus and hover stay in Theme.axaml, and the
properties they set do not overlap, so the compact slider looks as before). `Fx/DrawKit.cs` is new: `EqGraph`'s brush and text helpers
and the frequency/gain texts of `EqBandItem` moved there, so the two editors share them. `Fx/GraphicEqDrawing.cs` is the base of the
three drawn pieces.

**Files.** New: `Audio/EqLayout.cs`, `Audio/GraphicEq.cs`, `Fx/IEqEditor.cs`, `Fx/GraphicEqItem.cs`, `Fx/GraphicBandItem.cs`,
`Fx/GraphicEqRow.axaml(.cs)`, `Fx/GraphicBandRow.axaml(.cs)`, `Fx/GraphicEqDrawing.cs`, `Fx/GraphicEqCurve.cs`, `Fx/GainScale.cs`,
`Fx/BandLabels.cs`, `Fx/SliderPaint.cs`, `Fx/DrawKit.cs`, `Styles/Sliders.axaml`. Changed: `Audio/Eq.cs` (preset shapes, `ShapeAt`,
`Layout`; `FromPreset` / `PresetNameFor` / `CustomName` moved to `EqLayout`), `Audio/EqMath.cs`, `Audio/FxModel.cs`,
`Audio/MixerSettings.cs`, `Audio/Engine/Dsp/ChainBuilder.cs`, `Audio/Engine/ApoChainWriter.cs`, `Services/AppSettings.cs`
(`EffectEditorPref.WideRange`), `Fx/FxSlotViewModel.cs` / `.View.cs`, `Fx/EqPanelItem.cs` (`IEqEditor`), `Fx/EqBandItem.cs`,
`Fx/EqGraph.cs`, `Pages/RowEditing.cs` (vertical sliders), `Styles/Theme.axaml`, `App.axaml` (templates, `GraphicEqCurveHeight`,
`GraphicEqSliderHeight`, `VBandThumbHeight`, the Sliders include), `AGENTS.md` (master slider list), `ARCHITECTURE.md`.

**Not verified.** Nothing was run, seen, heard or clicked; only clean compiles. In particular: (1) the Fluent vertical slider template
was not readable here (compiled into the theme DLL): that the minimum is at the bottom, that `Grid#VerticalTemplate` and
`Border#TrackBackground` are the parts the `.vband` styles reach, and that the knob travels the slider's height less `VBandThumbHeight`
(the dB scale's marks and `SliderPaint`'s heights assume it) are from memory of Avalonia's source; if the scale's marks do not sit beside
the knobs, that is the place to look. (2) The two `GridLength` resources in `App.axaml` compiled, but their use as row heights at run
time was not seen. (3) Whether the inner sideways scroll viewer takes the mouse wheel over the curve or labels (so the editor does not
scroll down while the pointer is there). (4) The curve's centres sit above their sliders only while the columns are equal (UniformGrid);
the ISO nominal centres are off the exact log spacing by up to about 1 %. (5) Raised neighbouring bands add up (as on a hardware
graphic EQ): a sampled preset can be up to about 2-3 dB louder on the 32-band set than the same preset on the Parametric; the curve shows
the true total, and the APO's Preamp takes it into account. (6) CPU with 32 bands not measured (each non-flat band is one biquad per
channel; flat bands cost nothing). (7) The narrow FX window and Scale 70 % with 32 bands; both themes.

**Open items.** No hotkeys were added: no effect inside an FX window has hotkeys today (rule 4 would need a design for "the selected
effect" first). The wheel's step on a band slider is the shared 1/100 of its span (0.24 dB at ±12), not a round 0.5 dB. A gain beyond
±12 dB set while the range was ±24 (or typed) shows at the slider's end; a key or wheel step from there moves it back inside the range.
The range is per kind (both graphic EQs keep their own), not per slot. The 16 Hz band of the 32-band set is below the Parametric's 20 Hz
limit; it is fixed and only its gain changes.

**Owner test steps.**
1. Open a chain that had an Equaliser: its row reads "Equaliser - Parametric", bands, preset and sound as before.
2. Add effect: the three equalisers appear one after another; add "Equaliser - 32 Bands" and "Equaliser - 16 Bands".
3. 32 Bands, FX window made as narrow as it goes: the bands scroll sideways with the master bar; labels every second one; Scale 70 % and
   100 %: everything shrinks and grows; the dB scale's +12/0/-12 sit beside the knob at those gains.
4. Keyboard: Tab into the editor; Left/Right walk the bands; Up/Down, Shift+Up, Ctrl+Up, PageUp, Home, 0; Esc, then Down to the
   Overall gain card; Esc again closes the window.
5. Mouse: drag a knob; double-click a knob (0 dB); wheel over a slider; press on the first slider and sweep right across all of them
   while moving up and down: each follows the pointer's height.
6. Presets on both: Flat, Bass boost, V shape, Treble boost... the curve and sliders follow, "Custom" after a change. Flat pill.
7. Range ±24: sliders reach 24 dB, gains unchanged; back to ±12.
8. Parameters: one card per band with the gain typed by double-click or Ctrl+Enter, then Overall gain.
9. Music through each (Full control, or a program in Fast mode): boost and cut while playing: no clicks. On a PC with Equalizer APO on the
   master: `OmniControl.txt` has one Preamp line and one `Filter: ON PK Fc ... Gain ... Q 4.318` (or 2.145) line per non-flat band.
10. Both themes.

## FX window header: total response graph and live numbers (14 Nov) - plan, written before coding (as-built notes in the next section)

> Superseded in part on 15 Nov ("FX window tidy, keyboard instructions off screen", the last sections): the keyboard footer strip, the CPU number, the Output picker in the header and the controls under and above the editor are gone; the toolbar row, the status strip and Options > Plays on / Keyboard shortcuts replace them.

Owner-approved (spec and mockup "Design A"): one header row, same height as today: Effects switch left (and a program's Output
picker), then the three live numbers Peak, Latency, CPU, then a 150 x 34 graph of the chain's TOTAL response at the right end.

**Data flow (where each number comes from, per engine path).**
- Which engine runs a row: one new field on the sound modes' master list, `ModeMethod.Engine` (`FxEngines.Apo` for "in Windows
  (Equalizer APO)", `FxEngines.Program` for "by recording" and "through the cable"); `mode.MethodFor(key).Engine`.
- Graph and Peak: `Audio/ChainResponse.cs` (new, no UI, next to `EqMath`): for every enabled effect of the chain that the row's
  engine really runs and whose response is known (every equaliser through `FxCatalog.EqLayoutOf`/`EqGainOf`, and Gain), its
  `EqMath.CurveDb` (new optional `wet`: the dry sound mixed back exactly as `WetStage` does, (1 - w) + w H) summed in dB; the peak over
  the grid and at every band's own frequency. Equalizer APO does not run a wet mix below 100 % (`ApoChainWriter`), so there such an
  effect is not drawn. Everything else enabled (compressor, limiter, delay, gate, reverb, IR Loader, VST3) is listed with its reason
  ("Shows EQ and gain only"). Recomputed from the stored chain on every edit (`FxChainViewModel.Edited`, raised where every edit is saved).
- Latency: `EngineRun.DelayMs` (the pipe's estimated delay + the plug-ins' reported latency, the number the row's status already
  shows; now one property used by both); Equalizer APO rows: "none added".
- CPU: `Dsp/CpuMeter.cs` (new): `ChainProcessor.Process` adds the block's processing time (Stopwatch ticks) and its real-time length
  to two counters with `Interlocked.Add` (no lock, no allocation on the audio thread); the FX window reads them every
  `Timing.FxAnalysisRefresh` (500 ms) and shows busy time / real time of the last period, smoothed, as % of one core. Equalizer APO
  rows: "not measured here". Effects off or not running: "-".
- `EffectsEngine.LoadOf(key)` -> `RowLoad` (new, `Audio/Engine/RowLoad.cs`): where the row runs now (none / Equalizer APO / own
  engine) with the delay and the CPU counters. The headroom sentence per engine (`EngineStatus.HeadroomNote`): own engine = the
  safety limiter keeps peaks below -1 dB, nothing is lowered in advance; Equalizer APO = each equaliser's level is lowered by its own
  highest boost (its Preamp), a Gain effect is not.

**View.** `Fx/FxAnalysisViewModel.cs` (numbers, scale, hover/pin state and timers; fed by `FxWindowViewModel`, no engine knowledge in
the view), `Fx/FxAnalysisBar.axaml(.cs)` (the numbers and the thumbnail, a `Button.graphthumb` keyboard stop; the enlarged graph in a
`Popup` holding a `FlyoutPresenter.graphmenu`, the master pop-down look, placed under the thumbnail's right edge over the panes),
`Fx/FxAnalysisNumbers.axaml(.cs)` (the three numbers, shared by the bar and the panel), `Fx/ResponseThumb.cs` and `Fx/ResponseGraph.cs`
(drawn: thumbnail; large graph with axes, grid, crosshair and readout), `Fx/ResponsePlot.cs` (the shared plot frame, frequency and dB
grid with labels, curve and fill geometry; `EqGraph` and `GraphicEqCurve` move onto it so nothing is drawn twice), `Fx/FoldingRow.cs`
(a row that drops the numbers when they would leave less than its `Reserve` for the controls beside it: the narrow window). Auto
scale: +-12 dB, widened in 3 dB steps to the curve's extreme + 1.5 dB (+24 -> +-27), at most +-30. Sizes in `App.axaml`
(`FxThumbWidth/Height`, `FxGraphPanelWidth`, `FxGraphHeight`, `FxOutputMinWidth`), times in `Timing.cs` (`FxGraphHoverOpen` 300 ms,
`FxGraphHoverClose` 150 ms grace for the gap between thumbnail and panel, `FxAnalysisRefresh` 500 ms). Styles in a new
`Styles/Graphs.axaml` (`Button.graphthumb`, `StackPanel.stats`, `TextBlock.statlabel/statvalue`, as built `.statvalue.caution` for the amber Peak).
Keys: Enter or a click on the thumbnail pins the panel open or closes it, Esc closes it before anything else; the thumbnail joins the
FX window's Tab cycle (Shift+Tab from the list).

## FX window header: total response graph and live numbers (14 Nov) - as built; compiled only (throwaway builds, 0 warnings, 0 errors); nothing run, seen, heard or clicked

> Superseded in part on 15 Nov ("FX window tidy, keyboard instructions off screen", the last sections): the keyboard footer strip, the CPU number, the Output picker in the header and the controls under and above the editor are gone; the toolbar row, the status strip and Options > Plays on / Keyboard shortcuts replace them.

**What it is now.** The FX window's first header line is a DockPanel: the Effects switch on the left, a program's Output picker in
the middle, and at the right end the analysis (`Fx/FxAnalysisBar`): Peak, Latency, CPU (11 px, muted label and Medium value, 10 px
apart), 12 px, then the 150 x 34 thumbnail (`Button.graphthumb`: card surface `BrushSidebar`, 1 px `BrushDivider` frame, radius 6;
hover `BrushHoverEdge`, keyboard focus and press the accent; only colours change). The thumbnail draws the chain's TOTAL response,
20 Hz-20 kHz on a log scale, 0 dB in the middle, the curve 1.5 px in the accent (muted while the row's effects are off), the scale
("±12") top right, and a caution-coloured dot on the peak when it is above +12 dB. The line under it (engine status, Fast-mode caution)
is unchanged.
- Total response: `ChainResponse.Of(chain, engine, grid)` on every edit (`FxChainViewModel.Edited`, raised in `SaveChain`, which every
  edit, reorder, power switch, wet change and paste passes) and when the row's on/off or the sound mode changes. Engine of the row =
  `mode.MethodFor(key).Engine` (new `ModeMethod.Engine`). Drawn: every enabled equaliser (Parametric, 32 Bands, 16 Bands: bands and
  overall gain) and Gain, each with its wet mix ((1 - w) + w H, `EqMath.CurveDb(..., wet)`), summed in dB over a 400-point log grid;
  in Equalizer APO a wet below 100 % is not run, so not drawn (`FxCatalog.ApoCanMix`, now shared with `ApoChainWriter`). The peak is
  the highest of the grid and of every non-flat band's own frequency (so +24 dB at 1 kHz reads exactly "1.00 kHz"). Not drawn, listed
  in the note's tooltip with the reason: compressor, limiter, delay, gate, reverb ("saved only"), IR Loader ("its response is not
  drawn" in the APO, "runs only in Equalizer APO" in the own engine), VST3 ("plug-in: its response is not known").
- Scale: +-12 dB; when the curve's extreme (up or down) is beyond 12 it widens to the extreme + 1.5 dB rounded up to 3 dB steps
  (+24 -> +-27, +13 -> +-15), at most +-30 (a deeper notch or pass-filter slope is drawn at the edge). Both graphs use it.
- Peak: "+24.0 dB" (caution colour above +12 dB), "-" while effects are off. Tooltip: "Boost of +24.0 dB at about 1.00 kHz." plus what
  the engine really does (`EngineStatus.HeadroomNote`): own engine = nothing is lowered in advance, the safety limiter keeps the
  output below -1 dB; Equalizer APO = each equaliser's bands follow a Preamp of minus their highest boost, a positive overall gain and
  Gain effects are not cut, no limiter. (The spec's example "The level is lowered by 24 dB automatically" is only true of the APO
  path, so the wording follows the engine.)
- Latency: own engine = `EngineRun.DelayMs` (the pipe's estimate + the plug-ins' reported latency, the same number the status line
  shows; the tooltip splits the two); Equalizer APO = "none added"; not running or off = "-".
- CPU: `ChainProcessor.Process` times every block (two `Stopwatch.GetTimestamp` calls, then `CpuMeter.Add`: two `Interlocked.Add`,
  no lock, no allocation). Every 500 ms (`Timing.FxAnalysisRefresh`) the window reads the totals and shows busy time / real time of the
  sound processed in the last period, smoothed half and half with the previous value, as % of one core ("0.3 %", whole numbers from
  10 %); the time covers the chain, the row's level and the limiter. A new run starts from its own first reading; a period with no sound
  processed shows "-". Equalizer APO = "not measured here".
- Larger graph: pointer on the thumbnail for 300 ms (`Timing.FxGraphHoverOpen`) opens a `Popup` (light dismiss off, so it never takes
  clicks or focus from the window) holding a `FlyoutPresenter.graphmenu` (the master pop-down look; 440 wide, 9 px padding) under the
  thumbnail's right edge, over the panes; nothing underneath moves. Inside: the 420 x 170 graph (`ResponseGraph`: dB labels every 12,
  grid every 6 dB and at 20/50/100/.../20k, the same scale, the curve 2 px, the peak dot (caution colour above +12 dB, accent for a
  smaller boost, none without a boost), a vertical line following the pointer with a dot on the curve and "1.00 kHz / +24.0 dB" beside
  it), the three numbers with "Peak +24.0 dB at 1.00 kHz", and "Shows EQ and gain only" when something is not drawn. It closes 150 ms
  (`Timing.FxGraphHoverClose`) after the pointer has left both. A click on the thumbnail (or Enter/Space on it) pins it open, a second
  one closes it, Esc closes it first (before the window's own Esc). The thumbnail is in the window's Tab cycle: Shift+Tab from the
  chain list goes to it, Tab from it to the list, Shift+Tab from it to the editor (the panel's own Tab still switches its two panes;
  the footer says so). No channel switch: the chain's response is the same on both channels.
- Narrow window: `FoldingRow` leaves the numbers out when they would leave the Output picker less than `FxOutputMinWidth` (240; 0 for
  rows without an Output picker); a 16 px margin keeps it from flickering as the numbers change width. Then only the graph stays and the
  hover graph shows the numbers. Decision: no separate tooltip on the thumbnail, because the hover graph opens at 300 ms, before a
  tooltip would (400 ms), and a tooltip would sit over it; the larger graph is the fold-out.
- Shared drawing: `Fx/ResponsePlot.cs` (`PlotFrame` and the axes, grid, labels, line and fill) and `Fx/ItemDrawing.cs` (`ItemDrawing<T>`
  over an `IRedrawSource`). `EqGraph` and `GraphicEqCurve` now draw through them (same sizes, pens, label places and order; only the
  code moved) and `EqGraph`, `GraphicEqDrawing` follow their item through `ItemDrawing` (they now subscribe only while on screen).

**Files.** New: `Audio/ChainResponse.cs`, `Audio/Engine/RowLoad.cs`, `Audio/Engine/Dsp/CpuMeter.cs`, `Fx/FxAnalysisViewModel.cs`,
`Fx/FxAnalysisViewModel.Panel.cs`, `Fx/FxAnalysisBar.axaml(.cs)`, `Fx/FxAnalysisNumbers.axaml(.cs)`, `Fx/ResponseThumb.cs`,
`Fx/ResponseGraph.cs`, `Fx/ResponsePlot.cs`, `Fx/ItemDrawing.cs`, `Fx/FoldingRow.cs`, `Styles/Graphs.axaml`. Changed: `Audio/EqMath.cs`
(wet), `Audio/FxModel.cs` (`ApoCanMix`), `Audio/SoundModes.cs` (`ModeMethod.Engine`), `Audio/Engine/ApoChainWriter.cs`,
`EffectsEngine.cs` (`LoadOf`), `EngineRun.cs` (`DelayMs`, `PluginDelayMs`, `Cpu`), `EngineStatus.cs` (`HeadroomNote`), `Dsp/DspChain.cs`
(block timing), `Services/Timing.cs`, `Fx/FxChainViewModel.cs` (`Effects`, `Edited`), `Fx/FxChainPanel.axaml.cs` (`ListHasKeyboard`,
`FocusEditor` public), `Fx/FxWindow.axaml(.cs)`, `Fx/FxWindowViewModel.cs`, `Fx/EqGraph.cs`, `Fx/GraphicEqCurve.cs`,
`Fx/GraphicEqDrawing.cs`, `Fx/GraphicEqItem.cs` / `EqPanelItem.cs` (`IRedrawSource`), `Fx/DrawKit.cs` (`SmallLabelSize`,
`ReadoutFreq`, `ReadoutDb`), `App.axaml` (sizes, include), `Styles/Menus.axaml` (`FlyoutPresenter.graphmenu`), `Styles/Theme.axaml`
(master list comment), `AGENTS.md`, `ARCHITECTURE.md`.

**Differences from the mockup and the spec.** The thumbnail's frame is 1 px in `BrushDivider` instead of 0.5 px (a 0.5 px frame does
not show keyboard focus; colours follow the app's palette, which is darker than the mockup's greys). The Peak's tooltip says what each
engine really does (see above). No tooltip on the narrow thumbnail (see above). The scale stops at +-30.

**Not verified.** Nothing was run or seen. In particular: (1) the header's height: on master and microphone windows (no Output picker)
the row was as tall as the Effects switch; if the Fluent switch is under 34 px, the row is now up to a few pixels taller (programs' rows
are set by the Output card, likely taller already). (2) The fold points: the numbers' widths were estimated (about 230 px for a program,
300 px for an Equalizer APO row: "none added", "not measured here"), so at 560 px both fold and at 860 px both show; not seen. (3) That
the Popup stays above a "Keep on top" FX window, follows the window when it is moved while pinned, and that Esc reaches the window while
the pointer is over the pop-up. (4) A `FlyoutPresenter` used outside a Flyout: its Fluent template (background, frame, corner) was not
seen in a Popup. (5) The CPU numbers on real audio; the wet curve against what is heard; both themes.

**Open items.** No hotkey for the larger graph (no effect inside an FX window has hotkeys yet; rule 4 needs a design for "this FX window"
first). A pinned graph does not close when the FX window loses focus. The IR Loader's response (its impulse file) is not drawn.

**Owner test steps.**
1. FX window of a program with "Equaliser - Parametric": the right end of the switch's line shows Peak, Latency, CPU and the small
   graph. Make one band +24 dB at 1 kHz: the curve rises, the scale label reads "±27", an amber dot sits on the peak, Peak reads
   "+24.0 dB" in amber; hover Peak: "Boost of +24.0 dB at about 1.00 kHz." and what the engine does. Set it back to 0: "±12", no dot.
2. Play sound through it (Full control, or Fast mode for a program): Latency shows a number of ms like the status line; CPU a small %
   that updates twice a second. Effects off: all three "-", the curve grey.
3. Master in Fast mode with Equalizer APO: Latency "none added", CPU "not measured here".
4. Add a Compressor (or a VST3): "Shows EQ and gain only" appears in the larger graph; its tooltip names the effect and why.
5. Hover the small graph: after about 0.3 s the larger graph opens below it over the panes; move into it: the vertical line follows
   with "x kHz / y dB"; move away from both: it closes. Click the small graph: it stays open when the pointer leaves; click again or
   Esc: it closes. Wet 50 % on the equaliser: the boost shrinks (not a plain shift of the curve).
6. Keyboard: in the chain list press Shift+Tab: the small graph gets the accent frame; Enter opens the larger graph, Esc closes it, Tab
   goes back to the list.
7. Make the FX window as narrow as it goes (560): only the small graph stays at the right; hovering it shows the numbers in the larger
   graph. Widen it: the numbers come back. Program and master windows, both themes.

## VST2 plug-ins, plug-in folders, VST2 in Equalizer APO (14 Nov) - plan, written before coding (as-built notes in the next section)

**What TabForge does (read-only clone of github.com/cobhc95/TabForge at 9bb7236, MIT, same owner).** `TabForge.AudioEngine/Plugins/Vst2Plugin*.cs`
hosts a 64-bit VST2 `.dll` in-process through its plain C interface with its OWN definitions (no Steinberg header): an `AEffect`
struct with `[StructLayout(Sequential)]`, the dispatcher opcodes as numbers (effOpen 0, effClose 1, effSetSampleRate 10, effSetBlockSize 11,
effMainsChanged 12, effEditGetRect 13, effEditOpen 14, effEditClose 15, effEditIdle 19, effGetChunk 23, effSetChunk 24, effGetParamLabel 6,
effGetParamDisplay 7, effGetParamName 8, effStartProcess 71, effStopProcess 72), flags (hasEditor 1, canReplacing 16, programChunks 32,
isSynth 256), function pointers (`delegate* unmanaged[Cdecl]`), `LoadLibraryEx` (own folder first, one retry in the standard order for
error 126), the entry `VSTPluginMain` or `main`, magic 'VstP', a host callback `[UnmanagedCallersOnly]` answering version 2400, getTime,
sample rate, block size, process level, sizeWindow (flag only), canDo and the Reaper probe; channel buffers allocated once in native
memory, processReplacing (or the old accumulating process with cleared outputs); state = the chunk when the plug-in has chunks, else
its own "parameter list" (mark + one float per parameter); parameters by index with name, display text and label; the module is never
freed after its entry ran (plug-ins crash when unloaded while their threads run). `TabForge.Core/Plugins/PeExports.cs` + `PeImage`
read a DLL's export names from the file without loading it (VST2 = exports VSTPluginMain or main).

**What is reused (ported and adapted, credited in NOTICE.txt):** the `AEffect`/`ERect`/`VstTimeInfo` layouts and the opcode/flag
numbers (TabForge's own definitions, which match the public VeSTige-style clean-room headers; no Steinberg SDK file is copied), the load
sequence (open, rate, block, mains on, start process), the processReplacing / accumulating fallback, the host callback's answers, the
never-unload rule, the PE export reader, and the parameter text calls. Not taken: MIDI, transport, isolation process, Wine, 32-bit bridge,
Reaper probe (no ReaPlugs need it to run as effects), static instance table (the host's own pointer goes into `AEffect.user`, so the
callback finds its instance without global state).

**The seam.** `Vst3ChainHost` and every consumer (`ChainBuilder`, `Vst3Stage`, `EffectEditorDock`, `PluginEditorArea`, `PluginEditorHost`,
`PluginParameterList`, `PluginParamItem`, `FxChainViewModel`) use the concrete `Vst3Effect` / `Vst3Editor` / `Vst3Param`. One small pair
of Core interfaces replaces them: `IPluginEffect` (process, channels, block size, latency, state, editor, parameters) and `IPluginEditor`
(attach, detach, size, resize event, offer, scale, move view, idle); `Vst3Param` becomes the format-neutral `PluginParam`. `Vst3Effect`
and the new `Vst2Effect` implement them; `PluginLoader.Load(format, ...)` picks the class. `Vst3ChainHost` becomes `PluginChainHost`,
`Vst3Stage` becomes `PluginStage`; nothing else in the chain, wet, latency, state, FX window, pop-out, Parameters or Scale code changes.
The plug-in's format is a field of its master entry: `FxCatalog.PluginFormats` (stored kind "vst" = VST3, never renamed; new kind
"vst2"), each with its tag ("VST3"/"VST2"), its engines (VST3: OmniControl's engine; VST2: OmniControl's engine and Equalizer APO) and
its Core format; every `slot.Kind == PluginKind` test becomes `FxCatalog.IsPlugin(slot)`.

**File plan.** Core/Engine/Audio: `IPluginEffect.cs` (interfaces + `PluginParam`), `PluginLoader.cs` (`PluginFormat`, load and file
check by format), `PluginWindow.cs` (moving a plug-in's child window, shared by both editors), `PeFile.cs` (machine and export names,
header only; `Vst3File` uses it too), `Vst2Abi.cs` (the definitions), `Vst2File.cs` (header check, VST2 exports, the registry's
VSTPluginsPath), `Vst2Effect.cs` (+ `.Host.cs` callback, `.Params.cs` parameters), `Vst2Editor.cs`, `Vst2State.cs` (OmniControl's saved
form: chunk or named parameter values, also read by the APO writer; built as `Vst2Settings.cs`). App: `Audio/Engine/PluginChainHost.cs` (renamed), `DspStages.cs`
(`PluginStage`), `Fx/PluginEditorArea.cs` (idle timer, `Timing.PluginEditorIdle`), `Fx/Plugins/*` (scan .dll VST2 in the VST2 folders
and the user's folders, kind tag, picker accepts .vst3 and .dll), `Services/AppSettings.cs` (`PluginFolders`), `Pages/FileItem` +
`FileRow` (folder picking: `FileKind.Folder`), `Pages/ButtonItem` (description can change: the scan status), App settings page (folder
cards, Scan now, search entries, hotkey "Scan for plug-ins"), `Audio/Engine/ApoChainWriter.cs` (VSTPlugin line), `ApoChains.cs`,
`ChainResponse.cs`, `Fx/FxAnalysisViewModel.cs` (APO latency unknown with a plug-in).

**Equalizer APO syntax (read from native/EqualizerAPO/filters/VSTPluginFilterFactory.cpp, helpers/VSTPluginInstance.cpp,
helpers/StringHelper.cpp at bbfcc3e).** `VSTPlugin: Library "C:\path\plugin.dll" ChunkData "<base64>"` for a plug-in with chunks
(applied with effSetChunk, index 1 = program chunk, only when the plug-in has the programChunks flag), otherwise pairs
`"<parameter name>" <value>` matched against effGetParamName (UTF-8) and set with setParameter (0..1, read with wcstof: invariant
decimals). Parts are split at spaces outside quotes; a quote inside a quoted part is written twice. A relative Library path is under
Equalizer APO's own `VSTPlugins` folder; OmniControl writes the full path. Equalizer APO looks only for `VSTPluginMain` (a plug-in that
exports only `main` is not run) and must load the plug-in in its own bitness (64-bit Windows audio engine: 64-bit only). It runs the
plug-in inside audiodg.exe (one instance per pair of channels it takes), without a window, and logs a crash and passes the sound
unprocessed. So: OmniControl saves a VST2's state as the program chunk (the same bytes Equalizer APO applies) or as named parameter
values; an enabled VST2 slot with wet 100 % is written as one VSTPlugin line with ChunkData or its named values; a slot never opened has no
saved settings and runs with the plug-in's defaults (said on the row's note); wet below 100 % is not written (as now); VST3 stays "saved
only, not run here". The row's note says Equalizer APO runs it inside Windows' audio engine with the settings saved in OmniControl
(changes in its window reach Windows when the window closes), and the tooltip that a plug-in that crashes can interrupt Windows audio.
Latency in the header is "not known" when the APO runs a plug-in; the curve lists it as not drawn (as now).

## VST2 plug-ins, plug-in folders, VST2 in Equalizer APO (14 Nov) - as built; compiled only (throwaway builds, 0 warnings, 0 errors); nothing run, no plug-in loaded, nothing written to Equalizer APO's folder

VST2 is a discontinued format: Steinberg no longer licenses its SDK and no new VST2 plug-ins may be made with it. OmniControl hosts
VST2 only so plug-ins users already own keep working; it uses no Steinberg file (the interface definitions are OmniControl's own,
from TabForge, see `Core/Engine/Audio/Vst2Abi.cs` and NOTICE.txt). The plan above held; differences are listed at the end.

**What was built.**
- **One plug-in seam.** `Core/Engine/Audio/IPluginEffect.cs` (`IPluginEffect`, `IPluginEditor`, `PluginParam`, which replaced
  `Vst3Param`), `PluginLoader.cs` (`PluginFormat` Vst3/Vst2; `Load` and `Problem` by format; the shared refusal sentences: missing,
  32-bit, instrument, Smart App Control). `Vst3Effect` and `Vst3Editor` implement the interfaces and are internal now; `MoveView` moved
  to `PluginWindow.cs`, the PE header read to `PeFile.cs` (machine and export names; `Vst3File` uses it). App: `Vst3ChainHost` is
  `PluginChainHost` (loads through `PluginLoader` by the slot's type; new `StatesSaved` event), `Vst3Stage` is `PluginStage`; the FX
  editors, the settings list and the dock take the interfaces. No chain, wet, latency, state, pop-out, Parameters or Scale code was
  duplicated.
- **Plug-in types as a master list.** `FxCatalog.PluginTypes` (`PluginType`: stored kind, tag, file ending, Core format, engines). VST3
  keeps the stored kind `vst` (old mixer.json files keep working); VST2 is `vst2`. Every `Kind == PluginKind` test is `FxCatalog.IsPlugin`.
  The chain row shows the tag (VST3 / VST2 / FX) with "VST2 plug-in" as tooltip; the Add effect window's kind filter, its file picker
  patterns (*.vst3, *.dll) and its texts come from the list.
- **VST2 host** (`Vst2Effect.cs`, `.Host.cs`, `.Params.cs`, `Vst2Editor.cs`, `Vst2Settings.cs`, `Vst2File.cs`, `Vst2Abi.cs`). Load:
  header check first (missing, not a PE file, no VSTPluginMain/main export, 32-bit, other machine: one sentence each), then
  `LoadLibraryEx` (own folder first, one retry for error 126, Smart App Control through `AppControl`), entry point, magic, effOpen,
  then refusals after effOpen: an instrument (isSynth or no audio input), no output, over 128 channels, no 32-bit process call. The
  pipe's rate and block size (`PipeFormat.SampleRate`, `CoreTiming.PipeMaxBlockFrames`), mains on, start process. Processing:
  interleaved in/out copied to channel buffers allocated once in native memory (no allocation per block), processReplacing or the old
  accumulating process with cleared outputs, mono plug-ins as on the VST3 path (`PluginStage` mixes L+R in, copies one output to both),
  extra inputs (side-chain) get silence, outputs past two are dropped; all under the same per-plug-in lock as VST3, which UI-thread
  changes also take. Latency: `initialDelay`, read live, so it reaches the row's delay and the FX header like a VST3's. State: the
  program chunk (effGetChunk index 1, the bytes Equalizer APO applies) for plug-ins with chunks, else every parameter's name and value;
  marked so `Vst2Settings.Read` knows it. Parameters: by index, name (effGetParamName), the plug-in's display text with its label, 0..1,
  continuous; the default (double-click reset) is the value right after loading; typing uses percent of the range (VST2 has no units of
  its own). Editor: effEditOpen into the host child window, its size from effEditGetRect (asked again once shown; 400 x 300 if it never
  says), effEditIdle every `Timing.PluginEditorIdle` (33 ms) from `PluginEditorArea`'s timer, a size the plug-in asks for
  (audioMasterSizeWindow, any thread) taken in that idle call and raised as `Resized`; not resizable by the host; no host scale: a Scale
  other than 100 % is refused as for VST3 editors that ignore it (drop-down greyed, Parameters offered). One editor per plug-in at a
  time. Host callback: allocation-free, never throws into native code, finds its instance through `AEffect.user` (no static table);
  answers version 2400, current id, time info (rate and sample position only), sample rate, block size, process level (real time on the
  audio thread), vendor/product "OmniControl", canDo sizeWindow, language English, ioChanged/updateDisplay; everything else 0. Unload:
  mains off, `user` cleared, effClose, buffers freed; the DLL itself is never unloaded once its code ran (TabForge's rule); at exit under
  `ExitGuard` like VST3.
- **Scanner and folders.** `PluginScanner` finds every type by its file ending and header only (`MaxDepth` 4 = the one depth limit);
  a .dll counts only if it exports VSTPluginMain or main and is 64-bit. `PluginFolders.All()` = the VST3 folders, Program Files\VstPlugins,
  Program Files\Steinberg\VSTPlugins, Common Files\VST2, Common Files\Steinberg\VST2, the registry's HKLM\SOFTWARE\VST\VSTPluginsPath,
  then the user's folders; every root is scanned for both formats. plugins.json stores the tag ("VST2"). `PluginLibrary` is ONE object
  made by `ShellViewModel` and shared by every FX window and App settings (before, each FX window read its own copy); it reads
  plugins.json when first needed; `FoundText` is the one result sentence ("Found 20 plug-ins: 15 VST3, 5 VST2.").
- **App settings > plug-in folders.** `AppSettings.PluginFolders`; cards after "When I close the window": "Plug-in folders" (master
  `FileItem`, now with `FileKind.OfFolder`, which opens the folder picker), one "Scanned folder" row per folder (master `ButtonItem`:
  the full path, Remove), "Plug-in scan" (Scan now; the card's line shows the progress and then the result: `ButtonItem.Description`
  can change now). All searchable; hotkey "Scan for plug-ins" (`app.plugins.scan`). The page gets the scan as two callbacks, so Pages
  still never reference `App/Fx`. The Add effect window's status line ends with "Use App settings > Plug-in folders for other places."
- **Equalizer APO.** An enabled VST2 slot at 100 % wet on a Fast mode APO row is one line
  `VSTPlugin: Library "<full path>" ChunkData "<base64>"` or `... "<name>" <value> ...` (names quoted, inner quotes doubled, invariant
  decimals); no saved settings = the line with the library only (the plug-in's defaults). Left out with its reason in the row's
  "saved only, not run here" list: file missing, or no VSTPluginMain (Equalizer APO calls only that). Wet below 100 % is left out as for
  every effect. VST3 is unchanged (saved only there). The row status says "plug-ins run inside Windows with the settings saved here,
  their delay not known" (instead of "no extra delay") and "default settings until its window is opened: <names>"; the FX window's
  per-effect note says "in Equalizer APO with the settings saved here", its tooltip the rest (no window there, changes reach Windows when
  the plug-in's window closes, a plug-in that misbehaves there can interrupt Windows audio). A plug-in's saved settings are written
  into the line whenever the plug-in host saves states (`StatesSaved` -> `EffectsEngine.Reconcile`; the files change only when the text
  does). The FX header's Latency reads "not known" on such a row (`ChainCurve.RunsPlugins`); the curve lists the plug-in as not drawn.
- **Wording corrected:** AGENTS.md "Honest reporting", README, ARCHITECTURE.md, NOTICE.txt (TabForge: what was taken, MIT/GPL; the
  VeSTige line), Fast mode's details text, the Add effect texts; the old "VST2 plug-ins (.dll) can't run in OmniControl" message is gone.

**Differences from the plan.** `Vst2State.cs` became `Vst2Settings.cs` (named after its type). The Reaper host-API probe was not
ported (ReaPlugs ask for it; without it they get 0; not verified that they then run). Tail size (effGetTailSize) is not used:
OmniControl plays a live stream and never stops a chain to let a tail ring out, so there is nothing to use it for.

**Not verified (nothing was run).** No VST2 plug-in was loaded, processed, opened or saved; no VSTPlugin line was read by Equalizer
APO; no screen was seen. In particular: (1) the AEffect layout and callback against real plug-ins (it is TabForge's, which has run
real plug-ins); (2) whether effEditOpen into Avalonia's NativeControlHost child works for every VST2 editor (DPI-unaware editors will
look small on a scaled display: VST2 has no host scale); (3) the program-chunk choice: a plug-in whose program chunk lacks part of its
state would lose that part (OmniControl and Equalizer APO alike; TabForge saves the bank chunk); (4) that Equalizer APO accepts the
base64 and the quoted names as written (read from its source only); (5) the App settings cards at the narrowest window and in both
themes, and focus after Remove (the removed card had the keyboard); (6) the folder picker on a network path.

**Risks of in-process VST2 hosting (said plainly).** A VST2 plug-in runs native code inside OmniControl: a crash or hang in it takes
OmniControl down (the engine journal restores the sound at the next start), and nothing can catch an access violation in its code.
Its DLL stays loaded until exit. The host finds itself through `AEffect.user`: a plug-in that writes that host field (against the
interface) would crash OmniControl at its next callback. In Equalizer APO the same plug-in runs inside Windows' audio engine
(audiodg.exe, no window, the audio service's account: plug-ins that need the user's files, a licence check or an iLok may not work
there); Equalizer APO catches a crash while processing and then passes the sound unprocessed, but a hang there stalls Windows audio.

**Open items.** (1) Out-of-process hosting (one helper per plug-in) would contain crashes for both formats; not built. (2) VST2 bank
chunk vs program chunk: if a plug-in is found that saves only part of itself in its program chunk, store the bank chunk for
OmniControl's own engine and keep the program chunk for Equalizer APO. (3) No VST2 programs (factory presets) picker. (4) Equalizer
APO runs one VST2 instance per channel pair; a mono plug-in runs once per channel there. (5) Changes made in a VST2's window on an APO
row reach Equalizer APO only when the window closes (each write makes Equalizer APO reload the plug-in, so it is not done live).
(6) MIT entries in NOTICE.txt (TabForge, NAudio, Avalonia...) give the copyright line but not the MIT permission text; shipping the
texts (a licences folder) is still to do. (7) The scan reads the export table of every .dll under the folders; a very large user folder
makes the scan slow (off the UI thread; it can be stopped in the Add effect window, not on App settings).

**Owner test steps.**
1. App settings: "Plug-in folders" > Choose… > pick a folder with a 64-bit VST2 .dll; a "Scanned folder" row appears with the path.
   Scan now: the line counts, then "Found N plug-ins: … VST3, … VST2." Remove the row, Scan now again: its plug-ins leave the list.
2. Full control, a program's FX window > Add: the VST2 shows with "VST2"; add it. Its own window shows inside the FX window; move a
   knob, double-click its row: it pops out with the knob as set; Put back. Parameters: the list of its settings; move one, the plug-in follows.
3. Play sound through the row: the effect is heard; the row's delay includes the plug-in's latency if it reports one.
4. Close OmniControl and start it again: the VST2's settings are as left.
5. Fast mode, the master's FX window: add the VST2, open its window, change something, close the FX window. In
   `C:\Program Files\EqualizerAPO\config\OmniControl.txt` the master's section has `VSTPlugin: Library "...dll" ChunkData "..."` (or
   names and values); the sound changes (Equalizer APO runs it). The row says "plug-ins run inside Windows with the settings saved
   here, their delay not known"; the FX header Latency "not known". Wet 90 %: the line goes, the row lists it as not run here.
6. Load VST file… with a 32-bit VST2 or a non-plug-in DLL: one sentence says why; a VST2 instrument: "This is an instrument, not an effect."

## FX window tidy, keyboard instructions off screen (15 Nov) - plan, written before coding (as-built notes in the next section)

Owner-approved layout (with a visual) and the new design principles 12 (no keyboard instructions on screen) and 13 (fit more,
clutter less).

**FX window, top to bottom.** Title bar and menu (FX, Options) as before; Options gains "Plays on" (a program's output device, see
below) and "Keyboard shortcuts…". Then ONE toolbar row (`Border.toolrow`, a new master in `Styles/Bars.axaml`: about 30 px high,
11 px type, 22 px controls) holding, left to right: the Effects switch with its on/off words; the selected effect's preset drop-down
(equalisers only), Wet label + compact slider + value, On check box, Parameters pill, Scale drop-down, one Pop out / Put back pill
(these come up from under and above the editor, same bindings, tooltips and remembered settings; hidden while nothing is selected);
a "…" pill (only when folded); a flexible gap; then two tiny stacked lines "Peak …" / "Latency …" (10 px, muted label + value, the
caution Peak kept) and the response thumbnail (now 120 x 24) with its ±N label, hover graph and pin unchanged. Below it the two panes:
the effects list (narrower: 170 px) with small Add / Remove pills at its foot, and the editor area with ALL the remaining height
(nothing under it). At the very bottom ONE thin status strip (`Border.statusstrip`, 10 px muted, one line, trimmed, tooltip = full
text): Fast mode's recording caution (caution colour, only when it applies), the engine status line, and the selected effect's
"Runs in …" note at the right end only if it fits.

**Folding (narrowest FX window 560 px).** `Fx/FoldingRow` is extended instead of adding a second piece: children may carry
`FoldingRow.FoldLevel` (1, 2 …: folded in that order while the row is too wide), `FoldingRow.ShowsWhenFolded` (N: shown only while
level N is folded: the "…" pill) and `FoldingRow.Fill` (takes the width left over: the gap in the toolbar, the engine text in the status
strip, which then trims). Level 1 = Peak/Latency (they stay in the hover graph); level 2 = Parameters, Scale, Pop out, which then live
in the "…" pill's `MenuFlyout` (mouse first, the master menu look; the same items as the FX menu). The old `Reserve` and the
`besideoutput` rule go (no Output picker in the header any more).

**Output picker.** The approved row has no room for a program's Output drop-down, so it moves to Options > "Plays on" (radio items:
Master (the default device) and each device; several devices shows as on the Volume page). Decision the owner can overrule.

**Removed.** The FX window's keyboard footer strip and its strings; the effect count that sat in it (the list numbers the rows);
the Output picker in the header; CPU everywhere: the header and panel number, `FxAnalysisViewModel.CpuText/CpuTip` and smoothing,
`Dsp/CpuMeter.cs`, `ChainProcessor`'s block timing, `EngineRun.Cpu`, `RowLoad.Cpu` / `CpuSample`; the "Add... Ctrl+N" / "Remove Del"
key captions on the list buttons (the tooltips name the keys).

**Keyboard instructions app-wide (principle 12).** Every long hint paragraph at the bottom of a page or window goes: Volume (both
tabs), Mouse and Trackpad, Gestures, Displays, OSD, Shortcuts, App settings, the Parametric and graphic equaliser editors, the rule
editor, the master picker (both pages). Facts that are not about keys move to tooltips: a new `CardGroup.Tip` shown on the shared
group header (one place, every group can carry its facts), a `Tip` on `ToggleItem` / `GalleryItem` (OSD's "best-effort", the accent
note), the equaliser graphs' mouse gestures on the graphs, Test's "where the pointer is". Short captions that carry state stay
(App settings' start-up note, the Combine window's "Tick at least two devices", the Shortcuts page's one-line caption, the OSD page's
intro; the wording pass that follows this job may trim them).

**Where the shortcuts live.** Each page owns its text: `PageViewModel.KeyHelp` (virtual); the keys every card list shares are one
constant (`PageViewModel.CardKeys`), the shell's own keys one constant in `ShellViewModel`; shared windows keep theirs beside their
model (`PickerModel.KeyHelp`, `RuleEditorViewModel.KeyHelp`) and the pages that open them add them. The main window's footer gets a
"Keyboard shortcuts" pill (and F1) that opens the existing small `MessageWindow` with the open page's text, the card keys and the
shell's. The FX window's Options > "Keyboard shortcuts…" opens the same window (owned by the FX window) with the chain's keys and the
equaliser editors' keys (`EqPanelItem.KeyHelp`, `GraphicEqItem.KeyHelp`). All key handling and tooltips stay. The search index never
held the hint texts (search lists editors only), so it does not change.

**Light density pass (principle 13).** Only cheap, clearly better changes in the files touched; further ideas go to open items.

## FX window tidy, keyboard instructions off screen (15 Nov) - as built; compiled only (throwaway builds, 0 warnings, 0 errors); nothing run, seen or clicked

The plan above held; differences are listed under each part.

**FX window (`Fx/FxWindow.axaml`).** Rows: menu, toolbar (`Border.toolrow`), panes, status strip (`Border.statusstrip`).
- Toolbar, left to right, in one `FoldingRow` (gap `ToolRowGap` 6): Effects switch ("Effects on/off" words, Fluent's 154 px minimum
  width overridden to 0 for this switch only, a -5 px vertical inset fits its 32 px template into 22 px); preset drop-down (equalisers,
  `FxToolPresetWidth` 96); "Wet" + `Slider.compact` (`FxToolWetWidth` 56, double-click = 100 %) + value; "On" check box; Parameters
  pill (`.custom` while on); Scale drop-down (no "Scale" word any more: its tooltip says Scale and names Ctrl+- / Ctrl+= / Ctrl+0); one
  Pop out / Put back pill (`FxChainViewModel.PopText` / `TogglePopOutSelectedCommand`); "…" pill; fill; Peak / Latency as two 10 px
  lines (`FxAnalysisNumbers.stacked`, tooltips unchanged, caution Peak kept); the thumbnail, now 120 x 24 (`FxThumbWidth/Height`),
  with its ±N label, hover graph, pin, Esc and Tab stop unchanged. The selected effect's controls are hidden while nothing is selected
  (the preset also when the effect has none). All bindings, remembered settings (scale, Parameters per effect type) and tooltips are
  the ones the controls had under and above the editor.
- Folding: level 1 = Peak / Latency (they stay in the hover graph), level 2 = Parameters, Scale, Pop out; the "…" pill shows only while
  level 2 is folded and opens a `MenuFlyout` (the master menu look, mouse first) with Parameters (plain list), Smaller editor, Larger
  editor, Editor at 100 %, Pop out / Put back. Widths estimated at the default 860 px: everything shows for an equaliser (about 830 px
  needed); at 560 px both levels fold (about 536 px needed of 544). A plug-in (no preset) has about 100 px more.
- Panes: the effects list is 170 px (`FxChainListWidth`, was 246); Add… / Remove / Forget are small `Button.pill`s at its foot
  (tooltips name Ctrl+N and Delete; "Forget program" is now "Forget", its tooltip says what goes). The editor takes all the panel's
  height; only the "Select or add an effect" / "Loading" notice with its Put back pill is left inside it. Pane margin `FxPanesMargin`.
- Status strip (10 px, muted, one line, `StatusStripPadding` 8,2): Fast mode's recording caution as caution-coloured words (trimmed
  at `StatusCautionMaxWidth`, the old tag's tooltip), the engine line (fill: trims with "…", full text as tooltip), and the selected
  effect's "Runs in …" note at the right end only when the whole line fits (`FoldLevel` 1; its detail tooltip kept). Difference from
  the plan: the caution is coloured words, not a `Border.tag` (a tag would make the strip taller than one thin line).
- Options menu: Chain on, **Plays on** (a program's output device as radio items, rebuilt only when the devices or the choice change;
  "Several devices (see the Volume page)" shows checked when the Volume page set more than one), Keep on top, **Keyboard shortcuts…**
  (the window's keys, `FxWindowViewModel.KeyHelp`, in a `MessageWindow` owned by the FX window).
- Removed: the keyboard footer strip and its strings, the effect count in it (`FxChainViewModel.EffectCount`), the header's Output
  `ChoiceItem`, the old DockPanel/WrapPanel header, `Button.scope.fxbtn` (Theme.axaml), `FxHeaderGap`, `FxOutputMinWidth`,
  `FxAnalysisGap`, `FoldingRow.Reserve` and the `besideoutput` style, the "Ctrl+N" / "Del" captions on the list buttons.
- CPU removed everywhere: the number and its tooltip (header and hover graph), `FxAnalysisViewModel` smoothing and `CpuText/CpuTip`,
  `Audio/Engine/Dsp/CpuMeter.cs` (deleted), `ChainProcessor`'s per-block `Stopwatch` timing, `EngineRun.Cpu`, `CpuSample` and
  `RowLoad.Cpu`. `RowLoad` keeps the place and the delay; Latency still refreshes every `Timing.FxAnalysisRefresh`.

**Keyboard instructions off every page and window (principle 12).** Removed: the hint paragraphs at the foot of Volume (both tabs),
Mouse and Trackpad, Gestures, Displays, OSD, Shortcuts and App settings; the Parametric and graphic equalisers' hint lines; the rule
editor's hint; both hint lines of the master picker (its footer row now shows only on the settings page, for "Use this …"); Displays'
"Refresh (F5)" is "Refresh" (F5 in its tooltip). The facts in them that are not about keys moved to tooltips: `CardGroup.Tip` on the
shared group header (Displays Hardware and Software, Volume Outputs and Wiring Outputs, Mouse Buttons, Trackpad, Pointer settings,
the four Gestures groups and Custom rules), `ToggleItem.Tip` (OSD "Hide Windows' own OSD": best-effort), `GalleryItem.Tip` (App settings
accent: used everywhere, the OSD can match it), the rule editor's Test ("where the pointer is"); the equalisers' mouse gestures went into
their entries of the FX window's Keyboard shortcuts (a tooltip over a graph would cover the points being dragged). Kept on screen
(state or a limit, not keys): App settings' start-up note, Volume Wiring's "None yet" and the combine gate note, the Combine window's
"Tick at least two devices", the Shortcuts page caption, the OSD page intro, the Sound mode panel's footnote.

**Where the shortcuts live.** `PageViewModel.KeyHelp` (each page's own keys; Displays, Volume, OSD, Mouse and Trackpad, Gestures,
Shortcuts have one; App settings needs none beyond the card keys), `PageViewModel.CardKeys` (the keys every card list shares, once),
`ShellViewModel.ShellKeys`; `PickerModel.KeyHelp` and `RuleEditorViewModel.KeyHelp` are added by the pages that open them;
`EqPanelItem.KeyHelp` / `GraphicEqItem.KeyHelp` by the FX window. The main window's footer has a "Keyboard shortcuts" pill after the
version (and F1, not while a hotkey box is listening); it opens the small `MessageWindow` with the open page's keys (not while the
search results show), the card keys and the window's. `MessageWindow.AskAsync(owner)` gained the optional owner. The footer's status
text now trims (tooltip = full text) so the pill never pushes it over the Close button. The search index never held the hint texts.

**Light density pass (principle 13).** Done: the FX window above. Not changed (left for the next pass, see open items): pages already
use half cards where a card is small (Displays, Mouse, Gestures areas, OSD switches, App settings start-up pair).

**Mouse-first choices (rule 5; the owner can overrule).** The "…" menu and Options > Plays on are menus (keyboard reachable through
the menu bar's Alt keys for Plays on; the folded controls through the FX menu's identical items and Ctrl+- / Ctrl+= / Ctrl+0). The
toolbar's controls are not in the window's Tab cycle (Tab still switches list and editor, as before, when the same controls sat under
the editor).

**Not verified.** Nothing was run or seen. (1) The toolbar's real widths: the fold points were estimated from text sizes; if the
Fluent switch ignores the minimum-width override (the template may read it as a fixed value), the switch is 154 px wide and level 2
folds earlier (about 900 px). (2) The switch's -5 px inset assumes Fluent's 32 px switch template. (3) The `MenuItem` container theme
(`BasedOn` Fluent's MenuItem theme) and the radio marks of Plays on. (4) The status strip's height (about 17 px expected). (5) That the
"…" flyout and the hover graph still close and open as before; both themes; the Parametric EQ's editor height at Scale 100 % (about
540 - 22 menu - 30 toolbar - 17 strip - 8 margins = 460 px for the panel, was about 390).

**Open items (for the wording and space pass that follows; file: what).**
- `Pages/Osd/OsdPage.axaml`: the intro caption ("The on-screen display appears when …") could become the page's tooltip or go.
- `Pages/Shortcuts/ShortcutsPage.axaml`: the caption "Hotkeys work anywhere in Windows while OmniControl is running." could be a tooltip.
- `Pages/Volume/SoundModeButton.axaml`: each mode card has a description, a method summary and a footnote (three grey lines).
- `Pages/Volume/VolumePage.axaml`: "None yet. + Combine devices joins …" is a sentence; a short "None yet" with the detail as tooltip would do.
- `Pages/Volume/CombineWindow.axaml`: "Tick at least two devices. Speakers play …" could be one short line plus tooltip.
- `Fx/FxSlotViewModel.cs` `RunsNote`: the notes are long ("Runs in OmniControl's engine (not in Equalizer APO, which …)"), so in the
  status strip they fold away often; shorter notes with the detail in `RunsDetail` would show more often.
- `Fx/FxAnalysisViewModel.cs` tooltips (Peak, Latency) and the engine status texts (`Audio/Engine/EngineStatus.cs`) are long sentences.
- `Pages/AppSettings/AppSettingsPage.axaml`: the "Appearance, startup, window and plug-ins" section title repeats the card names.
- `Pages/Osd/OsdPageViewModel.cs` Customize group: five full-width cards (position, style, accent, background, opacity); position and
  opacity could pair as half cards if the galleries keep their width.
- `Pages/Gestures/GesturesPage.axaml` Edge group: one full-width card in its own group; it could join the Areas group.
- `Fx/EqPanelRow.axaml`: the view switches (tabs, grid, phase) take a row under the band controls; they could move into the "…" menu
  or a small toolbar in the editor.
- `Pages/Displays/DisplaysPage.axaml`, `Pages/Gestures/GesturesPage.axaml`: header tools (Refresh, Add rule) and the group-header
  tooltips now carry facts that used to be on screen; check that the owner finds them (a small (i) mark on headers with a tip is an option).
- No keyboard shortcuts entry yet for the popped-out effect window, the Add effect window, Manage mouse layers and the About window
  (they have no strips; their keys are the obvious ones).

**Owner test steps.**
1. Open a program's FX window with "Equaliser - Parametric" at the default size: one toolbar row under the menu: Effects switch,
   "Custom ▾", Wet slider and number, On, Parameters, a Scale drop-down, Pop out, then two tiny lines "Peak …" / "Latency …" and the
   small graph at the right. No text strip of keys at the bottom; one thin grey line with the engine status (hover it: the full text).
2. Scale 100 %: the EQ editor now takes the whole height between the toolbar and the status line; nothing under it.
3. Click Pop out: the editor opens in its own window and the pill reads Put back; click it: back. Parameters and Scale act as before.
4. Drag the window to its narrowest (560 px): the row never wraps; Peak/Latency go (hover the graph: they are in the larger graph),
   then Parameters, Scale and Pop out go and a "…" pill appears; its menu has them. Widen again: they come back.
5. Options > Plays on (program window): the devices with a mark at the current one; pick another; the Volume page follows.
   Options > Keyboard shortcuts…: a small window lists the FX window's keys; Esc closes it.
6. Fast mode, a program whose sound can't be recorded: the caution shows in amber at the left of the status line.
7. Every page (Displays, Volume Mixer and Wiring, OSD, Mouse and Trackpad, Gestures, Shortcuts, App settings): no paragraph of key
   instructions at the bottom; hover a group header (Hardware, Software, Outputs, Mouse Buttons, Trackpad, Pointer settings, the
   Gestures groups, Custom rules): its facts show as a tooltip. Hover OSD's "Hide Windows' own OSD" and App settings' Accent color.
8. The footer's "Keyboard shortcuts" (or F1) on each page: the page's own keys, then the card keys, then the window's. In the rule
   editor and the action picker no hint lines; the Gestures and Mouse pages' shortcuts list their keys.
9. Both themes; the narrowest main window (720 px): the footer's pill, status and Close fit on one line.

## Wording and space pass (first, light round)

Done: removed the OSD and Shortcuts intro captions and App settings' "Appearance, startup, window and plug-ins" title; the Sound mode
cards lost their method line (it is the first line of the card's tooltip now, `SoundModeCard.Tip`) and the footnote is one short
sentence; Wiring's "None yet" and the Combine window's "Tick at least two devices" keep the detail in a tooltip; `RunsNote` texts are
short ("Runs in OmniControl's engine only", "Runs in Equalizer APO only", "Saved only: not processed yet"). Shared: group panel padding
8,4,8,2 -> 8,3,8,2 and margin 0,0,0,6 -> 0,0,0,4 (`Theme.axaml`); main content margin 32,16,32,28 -> 32,12,32,24 and page title gap
12 -> 8 (`MainWindow.axaml`). Not verified: nothing run or seen; compile-checked only.
Skipped (open): long `EngineStatus` / `FxAnalysisViewModel` tooltips, Gestures Edge group into Areas, OSD Customize half cards,
the EQ view switches row, other pages' card labels and search subtitles (not scanned in depth).

## Wording and space pass, round 2

Per page (all texts only; ids, keywords, stored values unchanged):
1. Displays: monitor status texts ("Reading the monitor...", "No DDC/CI answer...", "Values not reported"), Screen power and Reset all tooltips.
2. Mouse and Trackpad: pointer labels (Swap buttons, Pointer precision, Characters per tilt notch, Hide while typing, Locate pointer on Ctrl), unavailable reasons, button tooltips, two action titles. Cards were already half cards. Skipped: the 17 cursor-role names (Windows' own names).
3. Gestures: the "Edge size" group is merged into the Areas group (strip card is the last full-width card there; its tooltip text joined the Areas tip; the stored group key "Gestures/Edge" is no longer used); group tips, rule editor tooltips and the "Allow this command?" and delete texts shortened.
4. OSD: Customize group uses half cards (Position and Opacity pair at the end; Style, Accent, Background stay full width: galleries are not half cards).
5. Volume: sound mode cards lost the "Selected" tag (the card's own selected style says it); mode, tag and status tooltips, setup button tooltips, Wiring and group tips, cable sentences, row tooltips, "Not playing yet?" next to Add program.
6. FX window: toolbar, status strip, chain panel, popped-out window tooltips; `EngineStatus.HeadroomNote`, `FxAnalysisViewModel` Peak and Latency tips (two short sentences); equaliser switches "Tabs / Grid / Phase". Skipped: moving the view switches into a menu (layout change, owner to decide).
7. App settings: "When closing" label, accent tip, startup note, plug-in folder texts.
8. Shortcuts: three wordy action titles shortened (ids unchanged).
9. Dialogs: Equalizer APO and OmniControl APO confirmations, swap-trial text, close choice text.
10. Main window: "No settings match." Footer and search subtitles unchanged (already one line).
No shared style or resource values changed in round 2. Not verified: nothing run or seen; compile-checked only.
