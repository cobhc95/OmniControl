# Agent rules

Read this file, then `ARCHITECTURE.md` (where things live), then `HANDOVER.md` (what was built and how it is wired).

## Working rules (from the owner)

1. **No visuals** (mockups, diagrams, widgets, screenshots) unless the owner explicitly asks. When asked, show the visual first and build only after approval.
2. **Finish = build, stop, relaunch.** When work is finished, build, stop any running `OmniControl.exe`, and launch the app for the owner. Stop the app before building or the build fails with locked files.
3. **One feature at a time**, scalable: a new page is a view-model, a view, one line in `ShellViewModel`, and one DataTemplate in `App.axaml`.
4. **Every feature registers bindable hotkeys** (`HotkeyRegistry.Register(new HotkeyAction(...))`), e.g. brightness up/down. They appear automatically as searchable cards in Shortcuts. For variable devices (monitors, speakers...) pass a device source so the user can pick all or a subset.
5. **Keyboard and mouse both work everywhere.** Keyboard focus shows the accent colour (no default white outline); every clickable item has a hover effect (styles in `Styles/Theme.axaml`). Up/Down wraps at both ends (sidebar and page lists).
6. **Never overlap or clip.** No fixed-width text columns: use `Width="Auto"` with a `SharedSizeGroup` inside `Grid.IsSharedSizeScope`, and give text that can grow `TextWrapping="Wrap"` or `TextTrimming`. Before finishing a UI change check the longest label, the narrowest window (minimum width in `MainWindow.axaml`), the search-results view, and both themes.
7. **Do not use the PC to test.** No simulated key presses or mouse input, no window automation, no changing system state (brightness, volume, registry, other apps). Reason from code and a clean build; the owner tests. (Past automated tests changed the owner's real settings.) Say what was and was not checked.
8. **Keep concerns separate.** Dependencies point down only: Core never references App, services never reference pages, pages never reference each other. A view-model never calls Win32/COM directly; views hold layout only. One concern per file, no file over 700 lines (split by concern; a `partial` class is fine). Keep `ARCHITECTURE.md` current.
9. **No hard-coding.** Intervals and grace periods live in `App/Services/Timing.cs`; colours come from theme resources and `AccentService`, never literals in views; persisted values live in the settings classes.
10. **Tidy as you go.** Remove dead code, stale notes and unused files when you touch an area; do not leave TODO notes in code (put open items in `HANDOVER.md`).

## Conventions the owner has set

- Orange accent by default, soft/hover tints kept transparent (`AccentService`); the theme and accent choice are Left/Right gallery cards, not drop-downs.
- Cards: label left, control right; keyboard Up/Down moves between cards, Left/Right edits or walks parts of a row, Enter activates. Half-width cards (`Half = true` on a `ToggleItem`) pair two per row and Up/Down follows reading order.
- Honest reporting: FX chains are stored settings only (no audio engine yet); say so rather than imply processing.
- Windows icon cache may show the old icon until Explorer restarts.

## Code quality rules

11. **Understand before you change.** Read the files around your change, and the nearest existing example of the same kind of thing (another page, card, service or hotkey), before writing anything. Copy that pattern; do not invent a second way to do something the code already does. If the existing way is wrong, say so and fix it everywhere, or leave it. No one-off exceptions.
12. **Keep code pieces loosely coupled.** A piece of code should know as little as possible about the others. Call another class only when your job truly needs its result.
    - Ask "who owns this?" before each call. If another class owns the data or rule, ask that class. Do not reach into its internals, duplicate its logic, or copy its state.
    - No chaining through objects (`a.B.C.D`), and no reading another class's private details through casts, reflection or shared statics.
    - Prefer a small interface, a callback (`Action`) or an event over a direct reference to a concrete class (as `MixerItem` reports changes).
    - No circular references. If two classes need each other, one is doing the other's job: move the shared piece to a third place.
    - No global mutable state. A static is fine for constants and pure helpers only.
    - Each method does one thing; a class has one reason to change.
13. **Put each piece of logic in one place.** No copy-paste: if the same logic appears twice, move it to one helper. Rules, limits and defaults are defined once and referenced; never retype a number or string. Views do not make decisions: the view-model decides, the service acts, Core talks to Windows (rule 8).
14. **Keep the design consistent.** Before adding anything, say in one sentence which layer it belongs to and why; if it does not fit cleanly, the design needs thought, not a workaround. Use the same word for the same idea everywhere. Prefer the simplest change that fits the existing design: no new abstraction, layer or dependency unless it removes real duplication or coupling today, and no features nobody asked for. Make things `private` or `internal` by default.
15. **Write safe, readable code.** No empty `catch` that hides failures: catch the specific exception, handle or log it, and comment why when not obvious (Windows calls can fail; keep the app running). Release what you acquire (COM objects, handles, timers, event subscriptions). UI work stays on the UI thread; slow work stays off it. Leave no commented-out code, dead code, unused parameters or debug output. Comments explain why, not what.
16. **Check your own work.** Before building, re-read your diff against these rules (coupling, duplication, hard-coding, wrong layer). Leave an area tidier than you found it within the current task; report bigger problems as open items in `HANDOVER.md` rather than fixing them silently. Update `ARCHITECTURE.md` and `HANDOVER.md` when files move or behaviour changes.
