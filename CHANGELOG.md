# Changelog

Versions go 0.1.0, 0.1.1 ... 0.1.9, then 0.2.0. Each release is a GitHub pre-release with a Setup and a Portable download.
New entries go under "Unreleased" as work lands; a release moves them under its version.

## Unreleased

## 0.1.3 (16 Nov 2026)

- Tray panel: in edit mode every slider and tile has a remove cross, and a right-click menu on any of them (or the menu key) removes it, changes its icon or moves it; removal is saved at once. The Choose-the-action window opened from the panel marks what is already in it ("In panel", a "Remove from panel" button, "Add again", an "In panel only" filter). Screenshot Record button is now an outline video camera.

- Tray panel: opens on the first click in well under 100 ms (a second click within the double-click time opens OmniControl instead). The panel is built once after start-up and only shown again; values come from the last readings. Sliders no longer show a white focus box, the Choose-the-action window opened from the panel can be moved, and long action names no longer get squeezed by their keycaps.

- OSD: the microphone pop-up now also shows when the mic is muted or its level changed from outside OmniControl (the laptop's mic-mute key, Windows settings, another app). It follows the default recording device and moves when that changes.

- Tray panel: the tiles and the dashed "+ Add" now flow in one grid of four columns; "+ Add" takes the next free cell instead of a row of its own.

- Icons: an action or state can have its own glyph, and the tray tiles, the Choose-an-action window and the Actions and hotkeys window show it (microphone, moon for night light, rotate, screenshot area / window / screen / all screens / record, effects, and a smart device's own chosen icon); an entry without one still shows its page's icon.

- Screenshot editor: the Rectangle tool (and the blur's frame while it is dragged) drew nothing when dragged up or to the left; it now draws in every direction. Pen, line, arrow, marker, text and blur were checked, also after a blur.

- OSD: the microphone level and mute pop-ups show a microphone (crossed out when muted) instead of the brightness sun; every OSD level now names its own icon, so none can fall back to brightness.

- Choose-an-action window: after a search the first result no longer shows a ghost row from the previous list; names wrap to two lines before trimming and the keycaps in the list are compact.

- Smart Devices: the device icon glyph is now centred in its rounded square (list rows, detail header and icon picker).

- The footer Close button now does exactly what the title-bar X does (follows App settings "When closing"). "When closing" offers Ask each time, Minimise to tray and Quit; the question has Minimise to tray, Quit and Cancel.

## 0.1.2 (16 Nov 2026)

- Tray panel: tiles now sit four to a row as designed, tile names are short ("Area", "Rotate") and wrap between words, and the speaker picker shows the device's short name instead of a trimmed one.

- Fixed: lists no longer run under their scroll bar (the "no key" at the right of the Actions and hotkeys entries was cut off); the master scroll bar style now reaches a list's own scroll viewer.

- Tray icon: a single click opens a small panel beside the tray with sliders (each screen's brightness, the volume with a speaker picker) and tiles (effects, each smart device, night light, area screenshot, microphone mute, rotate) that light up while on; "+ Add" adds a tile. Edit lets you add any level as a slider and any action or on/off as a tile, remove them, drag them into order and pick a tile's icon. A double click opens OmniControl; the right-click menu has Open OmniControl, Actions and hotkeys, Screenshot area, Record area, Pause all rules, master FX, App settings and Quit. The panel closes when you click elsewhere or press Esc. Saved in tray.json and included in backups.
- New actions: Quit OmniControl, Open App settings, Open Actions and hotkeys. New states: master effects on, microphone muted, warm light at night, all rules paused.
- Automation: two new kinds of trigger. "When a state changes" lists every state OmniControl knows (each smart device's on/off, master volume, master muted, default speaker, each monitor's brightness, contrast and other levels, the mouse and trackpad layer, on battery, battery charge, power mode): on/off turns on, turns off or changes; a level goes above, goes below or becomes a typed value; a choice becomes a value. The settings show the value now, when it was read and how it is known. "When an action runs" lists every action and starts the rule whenever that action runs, from a key, a mouse button, a gesture or another rule. A rule never starts itself, and a chain of rules starting each other stops after three. Both lists follow new devices, scripts, custom actions and renames at once, even while the window is open; a rule whose state or action is gone shows "Trigger missing" or "Action missing" and skips that part.
- Merging smart devices now also moves buttons, gestures and rules that used a merged device's On / Off / Toggle to the matching "via" action, and rules on its state to the device it was merged into.
- The Choose-the-trigger window no longer shows "Only with a hotkey" (it belongs to action lists only).
- Area video recording: a red Record button after Save in the screenshot overlay, and a new "Record area" action that records as soon as the area is drawn. While recording, the area has a dashed red frame and a small bar with the timer, Pause/Resume, Stop, and Microphone and System sound switches (MP4); both are left out of the video and the rest of the screen stays usable. Stop with the Stop button, Esc on the bar, or the action's hotkey again. MP4 (H.264, or HEVC when Windows has an HEVC encoder) through Windows' Media Foundation, on the graphics card when possible, or GIF made in OmniControl. Settings in the Video group of the area action and "Record area": format, frame rate, encoder, quality, system sound, microphone, show cursor, highlight clicks, save folder (Videos\OmniControl). Stops by itself at 4 hours (GIF 2 minutes), 16 GB (GIF 500 MB) or when the disk has under 1 GB free.
- Screenshot overlay: the colour applies to the text being typed, live; text size and line width step with Ctrl+Shift+. and Ctrl+Shift+, (also > and <), the A+ / A- buttons in the tool bar or the wheel over the colour, remembered per tool. The text box is quieter: a 1 px accent edge and the theme's selection wash.
- Choose-the-action window: settings can have a group heading (the screenshot actions' "Video").

- Screenshot overlay: both bars restyled (10 px panels, 30 px buttons, 2 px gaps, thin separators); the action bar is now Copy, Save | Search, QR, Print | Close, and the tool bar has a separator before Colour and Undo.
- Smart Devices (Scripts): the account email is handed to kasa trimmed and in lower case for Tapo and Kasa (a template flag), fixing "Device response did not match our challenge" when it was typed with capitals; the field keeps what was typed.
- Smart Devices: one device can have several methods (for example Scripts, then Simulation as a fallback). On, Off, Toggle and Check try them in the order shown and fall through when one fails; the status line says which method did it. Each method has its own set-up and its own "via" actions ("Lamp: On via Simulation") beside "Lamp: On". "+ add method" and drag to reorder in the Ready view; "Merge into…" in the list's right-click menu joins devices (set-ups and passwords kept, shortcuts moved). Existing devices become one-method devices.
- Smart Devices list: several devices can be chosen at once (Ctrl, Shift, Ctrl+A); right-click (or the keys) for Copy, Cut, Paste, Duplicate, Merge into… and Delete. A copy keeps every set-up except the password. The Ready view has a delete cross; its header now wraps under the name, and the password field keeps its Save pill whole at the narrowest window.
- Actions and hotkeys window: opens larger (1000 x 680) with a wider minimum so its three panes never clip; the keys column no longer hides under the scroll bar, the selected section's outline is fully visible, and "Restore Actions and hotkeys defaults" is listed under App settings instead of a section of its own.


- Smart Devices, sign-in: the password is saved only when you press Save (or Enter), with a "saved in Windows · HH:mm" tag, or "not saved" while typed text waits; Test uses the saved password and asks "Save the new password first?" when new text is waiting. The Save pill is part of the shared password field, and its eye is now centred.
- Smart Devices, Scripts: "profiles" are now "templates" (header chip "Template: Tapo"). Built-in: Tapo (all Tapo devices, one template; needs Third-Party Compatibility), Kasa (no sign-in) and Auto-detect (slower). Devices on Tapo plug, Tapo bulb or Kasa plug move to Tapo or Kasa. A template carries its commands, sign-in need and note, extra set-up stages and known-error tips, and the set-up stages are generated from it (Tapo adds "Tapo app setting"). kasa's noise lines are dropped from errors and known errors show a plain tip.
- Screenshots: four new actions (area with editor, active window, full screen under the pointer, all screens) in their own Screenshot section of the Choose-the-action window. The area overlay freezes every monitor; drag a selection, resize it with eight handles, move it by dragging or with the arrow keys (Shift = 10 px), draw on it (pen, line, arrow, rectangle, marker, text, blur, colour; drawings stay inside the selection, Ctrl+Z / Ctrl+Y), then Copy (Ctrl+C), Save (Ctrl+S), Print (Ctrl+P, Windows' print dialog), Search image online (copies the picture and opens Google Lens; nothing is uploaded by OmniControl) or Read QR code (on the PC). Save folder, file name ({date} {time} {app}), PNG/JPG/WebP/BMP, quality, Copy as image or image and file path, open folder, a note after saving and Stay on top (Normal / Strong) are settings, shared by the four actions unless switched off.
- Smart Devices: "Check now" in the Ready view reads the real state (Scripts: the profile's State command; Simulation: the taught state, emulator woken hidden) and updates the tag, list switch and icon. "Auto-check" per device: Off, When page opens, Every 5 min, Every 15 min (Scripts default: When page opens; Simulation: Off).
- Smart Devices redesigned: Ready and Setup. A device in the list shows its icon, its name and a switch with its last known state (click it to switch the device), or a "set-up" tag. A ready device shows its name, Method, "ready" and "on · read 14:52", On / Off / Toggle, Show emulator and Edit setup…; a new device or Edit setup… shows numbered stages, one open at a time, with Cancel (puts everything back) and Finish setup.
- Smart Devices, Simulation: "Teach on and off" replaces the closing Check. Press "It's on now" and "It's off now" and OmniControl takes several looks at the device's card (its words, switch states, icons, the button's colour from a screenshot and its shape) and keeps what tells them apart, so any app works, not only Tapo. On and Off tap only when the state is the other one, a run counts as working only when the state really changed, and when the state can't be read nothing is tapped and one sentence asks to teach again. The card is found by its name even after the list scrolled. If readings get unsure over time, the device says so and offers Re-teach. An old recording's closing switch Check is removed on load.
- Smart Devices, Simulation: the emulator runs hidden for actions from the page, the list, shortcuts and rules; only the set-up and Show emulator show it. The "Show the emulator window" switch is gone.
- Smart Devices, Simulation: the Emulator menu never trims names: Installed and Not installed sections, each row with its tags on their own line and a short note; not installed ones are dimmed with "Get it ↗".
- Smart Devices, Simulation: an app opened from the home screen is named by the words under its icon ("Tapo"), else its label, else the maker's part of the package ("Tplink"), never the last word of the package ("Iot").
- Smart Devices, Scripts: profiles. A device uses a built-in profile (Auto-detect, Tapo plug, Tapo bulb, Kasa plug; they update with OmniControl) or one of your own. Advanced… shows the On, Off and State command templates with {ip}, {email} and {password}; Test, Save as my profile…, Reset to built-in, Export and Import. Tapo profiles use kasa's smart protocol and let kasa work out the encryption, so a Tapo bulb (L530) works.
- Smart Devices, Scripts: kasa.exe is found where the new Python install manager puts it, the py launcher is followed to the real Python, and Python is asked where its programs are; `python -m kasa.cli` is used only when it works. A failed command shows kasa's own error line in red with Copy details (the log lines with email and password masked), not "ended with code 1".
- Smart Devices, Scripts: Test asks "Did it switch? Yes / No" unless the State command read the change back.
- Smart Devices: device icons. Each device has an icon (bulb, plug, lamp, fan, TV and more, a colour, or a picture of your own) in the list and the header, lit in its colour while the device reads on.
- Smart Devices: the Method menu shows two tags and a one-line brief per method.
- Smart Devices: typing a device's name renames it in the list, Shortcuts, Rules and the action picker on every key.
- Password fields have an eye: hold it (or hold Space or Enter on it) to see the password; letting go hides it.

- Actions and hotkeys: one window replaces the Shortcuts page and the All actions page. It is the Choose-the-action window (same three panes, search box and look), opened from the sidebar's bottom entry "Actions and hotkeys" and from search results. A thin top bar holds the shortcut set, Clear all and Restore defaults. Ctrl+F selects the search box; search finds actions by name or by key ("ctrl+o"), and pressing a key combination in the search box shows the actions on it. Each entry shows its keys as keycaps (or "no key"); "Only with a hotkey" filters the list. The right pane starts with the action's keys: an action can now have several keys ("+ add key"), each in its own box (double-click or Enter, then press the keys; Esc or a click elsewhere cancels; the cross removes it), "Suggest" binds its suggested key, a key another action has shows "Also on: ..." with "Move here", reserved keys are refused with one sentence; then its devices, Run now and Show on its page, then its settings. Saved keys from earlier versions read as one key each. Every Choose-the-action window (from a mouse button, gesture or rule too) shows the same keys and Hotkey part.
- The hotkey box everywhere (also the preset manager's shortcut column) starts listening on a double-click or Enter instead of a single click.
- Smart Devices, Simulation: steps show friendly names (the app's name, an element's own words, or "Tapo L530's device switch" built from its card and id) instead of package names, ids and coordinates; the exact matcher is in the tooltip and a position-only step reads "a spot on the screen".
- Smart Devices, Simulation: recording never records a tap on a page container (pager, list, layout); it uses the smallest tappable element, also from the screen just before or after the tap, and drops a tap that hits nothing. One tap gives one step. When several elements look alike (every device card's power button) the step keeps the card's name and the run picks the one in that card.
- Smart Devices, Simulation: a Wait for or Tap step scrolls the page up to 4 times to find its element before it fails.
- Smart Devices, Simulation: steps can be multi-selected (Ctrl+click, Shift+click, Ctrl+A, Esc clears); the right-click menu, Delete and Ctrl+C/X/V/D act on all selected steps.

- Smart Devices, Simulation: Show no longer restarts an emulator that already shows its window; it brings the window to the front. If the emulator runs hidden it asks first ("Restart it with its window? This takes about a minute."), and any start or restart reports live progress in the status line (Starting, Waiting for Android to start, Connected) with the pills off.
- Smart Devices, Simulation: installed emulators are now found reliably (known program paths plus Windows' Uninstall entries, so MuMu Player, BlueStacks, LDPlayer and MEmu show as installed) and checked again every few seconds. The Emulator menu lists Installed first (selectable) and Not installed dimmed with "Get it"; with no Android SDK adb, the emulator's bundled adb is used.
- Smart Devices, Simulation: Connect now works with an emulator that is stopped. A serial such as emulator-5554 is looked for in adb's device list instead of "adb connect", a stopped emulator is started (showing "Starting the emulator…"), and every failure is one plain sentence in the status line; the pill is disabled while it works.
- Smart Devices, Simulation: a "Keep running" chip (Always, Until idle N min, Close after each action), a state tag (running or stopped, hidden or shown, memory) and Show / Close now pills. An action wakes a stopped emulator by itself (hidden where the emulator can, quick boot after one good cold boot, one cold boot if a quick boot fails), waits until Android has started, and idle or close-after-action shuts it down cleanly. Start with Windows wakes it when OmniControl starts.
- Smart Devices, Simulation: MuMu Player 12 and MEmu added to the emulator list.
- Smart Devices, Simulation: right-click a step for Copy, Cut, Paste below, Duplicate and Delete; the remove cross sits at the right of each row and Delete works on a focused step.
- Smart Devices, Simulation: "The device's app is set up inside the emulator" has a Mark done pill and no longer waits for recording.
- Smart Devices, Simulation: Start recording / Stop recording, a plain sentence before recording and a status line while recording; the home screen is never recorded as the app and a tap that opens an app from it becomes "Open app".
- Smart Devices, Simulation: a step with nothing picked says so on its row, in the checklist and on Test ("Step 4 has nothing to check. Re-pick it or delete it."); the checklist follows the emulator opening or closing within seconds.
- Smart Devices: the Emulator menu shows each emulator's ease, memory need and a one-line note.

## 0.1.1 (16 Nov 2026)

- Smart Devices: opening an emulator no longer shows its console window, cold-boots (fixes the black screen from a broken quick-boot snapshot) and uses 2 GB of memory.
- Smart Devices: a newly installed emulator or adb is found automatically (checked every few seconds while missing), no restart needed.

## 0.1.0 (16 Nov 2026), first pre-release

- Displays: hardware and software controls, brightness and rotation presets, orientation screens.
- Volume: mixer with meters, per-app effects, wiring, combined devices, Easy mode, equaliser, VST2/VST3 and IR loader, presets.
- OSD, Mouse and Trackpad (layers, takeover, app scopes), Gestures.
- Automation: rules, custom actions, block rules, Scripts tab (PowerShell, CMD, Python).
- Smart Devices: smart plugs through scripts (python-kasa) or an Android emulator with recorded steps.
- Shortcuts page grouped by page, shortcut sets, Clear all; three-pane action picker.
- App settings: Backup and restore (export, import, wipe all).
- Sidebar grouped into DEVICES and WORKFLOW.
