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
