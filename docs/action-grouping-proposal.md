# Action list grouping proposal

Audit of every `HotkeyRegistry` registration (static, per-control and per-device). Goal: fewer rows in the Actions and hotkeys window, no function lost.
Terms: a **family** is one list entry holding several **members** (the existing actions, ids unchanged), each with its own keys, Run now, and mouse/gesture pick.
Counts are built-in entries only. User-made actions (programs, custom actions, scripts, Do rules, "Apply preset X", per-page "Restore X defaults") are not counted and not combined.

## Totals

| Section | Before | After | Optional extra |
|---|---|---|---|
| Displays | 93 | 54 | -3 (rotate) |
| Mouse and Trackpad | 27 | 21 | -1 (layers) |
| Volume | 15 | 12 | |
| Power (Automation) | 11 | 11 | -2 (modes) |
| Gestures | 2 | 2 | |
| OSD | 3 | 3 | |
| Screenshot | 5 | 5 | |
| App settings | 6 | 6 | |
| **Total** | **162** | **114** | **108** |
| Smart Devices (per device, M methods) | 3(1+M) | 1+M | e.g. 1 device, 2 methods: 9 -> 3 |

## Displays: Hardware (68 -> 39)

| Current | Combined entry | Before -> after |
|---|---|---|
| 29 level controls x (up, down): Brightness, Contrast, Red/Green/Blue gain, Saturation, Sharpness, Speaker volume, Red/Green/Blue black level, Backlight (white/red/green/blue), Gamma, Hue, Red/Yellow/Green/Cyan/Blue/Magenta saturation, Red/Yellow/Green/Cyan/Blue/Magenta hue | one entry per control, e.g. "Brightness" with members Up, Down | 58 -> 29 |
| Next colour preset, Next input source, Next display mode, Next speaker mute | unchanged (no "previous" exists) | 4 -> 4 |
| Turn screen off, Next monitor preset | unchanged | 2 -> 2 |
| Rotate to Landscape / Portrait / Landscape flipped / Portrait flipped | optional: "Rotate display" with four members (suggested keys kept) | 4 -> 4 (or 1) |

## Displays: Software (25 -> 15)

| Current | Combined entry | Before -> after |
|---|---|---|
| Digital brightness, contrast, colour temperature, gamma, red/green/blue gain, black level, white level (9) x (up, down) | one entry each, members Up, Down | 18 -> 9 |
| Saturation (all displays) up, down | "Saturation (all displays)" | 2 -> 1 |
| Toggle grayscale, Toggle invert colours, Reset all digital picture changes, Next display preset, Toggle warm light at night | unchanged | 5 -> 5 |

## Mouse and Trackpad (27 -> 21)

| Current | Combined entry | Before -> after |
|---|---|---|
| Pointer speed up/down, Double-click time up/down, Scroll lines up/down, Sideways scroll characters up/down, Pointer trail length up/down | "Pointer speed", "Double-click time", "Scroll lines", "Sideways scroll characters", "Pointer trail length" | 10 -> 5 |
| Next pointer scheme, Previous pointer scheme | "Pointer scheme" (Next, Previous) | 2 -> 1 |
| Toggle: precision, swap buttons, scroll one screen, ClickLock, pointer trails, hide while typing, locate on Ctrl, snap to default button | unchanged (8 independent switches) | 8 -> 8 |
| Reset pointer settings, Restore original pointer settings | unchanged | 2 -> 2 |
| Toggle app autoscroll, Toggle pause in full-screen games, Repeated clicking | unchanged | 3 -> 3 |
| Next mouse layer, Next trackpad layer | optional: "Next layer" members Mouse, Trackpad | 2 -> 2 (or 1) |

## Volume (15 -> 12)

| Current | Combined entry | Before -> after |
|---|---|---|
| Volume up, Volume down | "Volume" (Up, Down) | 2 -> 1 |
| Microphone level up, down | "Microphone level" | 2 -> 1 |
| Combined microphone level up, down | "Combined microphone level" | 2 -> 1 |
| Toggle mute, Toggle microphone mute, Toggle combined microphone mute, Toggle master FX, Toggle microphone FX, Show or hide closed programs, Switch Volume view, Next sound preset for everything, Restore audio defaults | unchanged | 9 -> 9 |

## Power (11 -> 11, optional 9)

| Current | Combined entry | Before -> after |
|---|---|---|
| Efficiency / Balanced / Performance mode | optional: "Power mode" with three members | 3 -> 3 (or 1) |
| Sleep, Hibernate, Shut down, Restart, Sign out, Lock, Pause all rules | unchanged | 8 -> 8 |

## Smart Devices (dynamic)

| Current | Combined entry | Before -> after |
|---|---|---|
| "Name: On / Off / Toggle" per device | one entry "Name" with members On, Off, Toggle | 3 -> 1 per device |
| "Name: On / Off / Toggle via Method" per method | one entry "Name via Method" with the same three members | 3 -> 1 per method |

## Unchanged sections

Gestures (Toggle gestures, Toggle gesture margins), OSD (Toggle OSD, Next OSD style, Toggle hiding Windows' OSD), Screenshot (5), App settings (Open OmniControl, Quit, Scan for plug-ins, Open settings, Open Actions window, Show or hide tray panel).

## Recommend NOT combining

- **Power: Sleep / Hibernate / Shut down / Restart / Sign out.** Each has its own countdown and Force close setting and is destructive; one row hiding them behind slots invites a wrong key.
- **Screenshot (5).** Each has its own editor and saved settings (`ActionEditor`); they are different outputs, not directions.
- **Toggle X next to its up/down** (Volume mute vs Volume up/down, warm light, grayscale, invert). Different kind of action and state; mute is not a direction of volume.
- **Hardware vs Digital** (Brightness vs Digital brightness). Different mechanism and scope (monitor vs desktop); keep in their own groups.
- **Pointer switches (8 toggles).** Independent features, nothing pairs.
- **Per-page "Restore X defaults", "Apply preset X", apps, rules, custom actions, scripts.** Made by the user or one per page in its own section; grouping would hide them.

## How it works technically

1. **Ids and storage stay as they are.** Every member remains a real `HotkeyAction` with its current id, key set, device scope and options in `hotkeys.json`; mouse, gesture, rule and preset references, `RunLevel`, `Find`, conflict checks and `Moved` are untouched. No migration.
2. **One new field on the master entry** (data lives on the entry, no side list): `HotkeyAction.Family` (`ActionFamily { Id, Title }`) plus `FamilyRole` (the member's word: "Up", "Down", "Next", "Previous", "On", "Off", "Toggle"). Level pairs need nothing new: `ForLevel` already knows `Level` (id, name) and `LevelDirection`, so family = `Level.Id`, title = `Level.Name`, role = Up/Down. Other pairs set `Family`/`FamilyRole` as init properties (pointer scheme, device power, optional rotate/layer/power modes).
3. **Grouping is a view-level projection, not a registry change.** The Actions and hotkeys window and the Choose-the-action list build rows from one small shared projection over `HotkeyRegistry.PickerActions`: members with the same family id become one row, others stay single. It is rebuilt on collection change (never a copy taken once), so registration order does not matter and re-registered device actions just regroup. Section, group and heading come from the first member.
4. **Settings pane of a family row:** one key block per member (its `HotkeyBox`es, "+ add key", Suggest, Run now, labelled with the role), then the shared settings once (device scope chips, step), written to all members (owner's principle 5; still stored per id as today). If saved values already differ between members the pane shows the first one until edited.
5. **Row display:** title = family title; keycaps show compactly (`Up Ctrl+Alt+Up  Down Ctrl+Alt+Down`); "Only with a hotkey" keeps a family when any member has a key; unavailable only when all members are.
6. **Search** still matches each member's own title and keywords: "Pointer speed down" finds the family row and focuses that member's block. `TextMatch` runs over the members.
7. **Mouse and gesture pickers keep picking one member:** they read `Actions` directly (or show the family as a heading with its members as selectable rows), so a mouse button stays bound to "Pointer speed up" by its unchanged id. Wheel-gesture level pickers already work per `Level.Id`.
8. **Needs:** one shared projection piece used by both windows, tests for grouping, search-by-member and "Only with a hotkey", and `ARCHITECTURE.md` / `HANDOVER.md` updated when built. Nothing built yet; the visual of the family row needs the owner's approval first (rule 0).
