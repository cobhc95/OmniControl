# Combining microphones: what exists, why it is unclear, and a design

Design only (14 Nov 2026). No production code was written, nothing was run, played or changed on the PC. Evidence is `file:line`
in this repo and the owner's `%APPDATA%\OmniControl\mixer.json` (read only).

## 1. What exists today

**The feature is built end to end in code, but it can never run on the owner's PC as set up now, and nothing on screen says so
until after the user has gone through the whole window.**

| Piece | What it does | Evidence |
|---|---|---|
| Data | `CombinedDevice` has `Kind` (`Speakers = 0`, `Microphones = 1`), `Members` (endpoint ids, order ticked), remembered `Cable`, and for microphones `FxOn`, `Fx`, `Volume`, `Muted` (one level and mute for the whole mix, none per member) | `Audio/CombinedDevice.cs:4-10, 24-36` |
| Window | "Combine devices": name box, two chips Speakers / Microphones (only when creating), "Tick at least two devices", ticked list, one cable line + setup pill, Create / Cancel. Opens on **Speakers**; picking Microphones clears the ticks and lists recording devices | `Pages/Volume/CombineWindow.axaml:13-27`, `CombineDeviceViewModel.cs:48, 67-75, 92-93` |
| Offered members | Every recording device that is not a virtual cable, so on this PC "Stereo Mix (Realtek)" and "Microphone Array (AMD)" | `VolumePageViewModel.Combine.cs:46-49` |
| Cable line | "CABLE is ready for it." when a cable is free, else "Needs another virtual cable: the new device waits until a cable is free." with a "More cables" pill | `VolumePageViewModel.Combine.cs:24-26, 52-59, 75-77` |
| Entry points | "+ Combine devices" pill in the Wiring top bar; a "Combine devices" search card; Edit on a combined card | `VolumePage.axaml:45-46`, `VolumePageViewModel.cs:88, 117` |
| Result in Wiring | A card in the separate "Combined devices" group: name, "= member + member", Cable column, setup pill, Edit, x | `VolumePage.axaml:52-67`, `CombinedCardView.axaml:16-29` |
| Result in Mixer | A row in the Mixer's Microphones group with a level slider, mute, effects and a "<- members" link that jumps to Wiring | `VolumePageViewModel.Inputs.cs:34, 76-91`, `InputItem.cs:28-42` |
| Planner | Full control only (Fast: every combined device is blocked "Needs Full control mode: a combined device runs through a virtual cable"). A combined microphone needs a cable with a recording side (`CableBox.Recordable`); members that are plugged in become the run's sources | `Audio/Engine/RoutePlanner.cs:104-108, 122-123, 187-201, 283-286`; `SoundModes.cs:188, 196` |
| Engine | `MicRun`: records every source, mixes them, applies the combined level/mute and the chain, plays into the cable's playback side ("CABLE Input"); status "Apps hear it on CABLE Output (VB-Audio Virtual Cable)" | `Audio/Engine/MicRun.cs:11-37`, `EffectsEngine.cs:219-220` |
| Mixing | `AudioPipe` mixes each capture ring into the block on the cable's playback clock | `Core/Engine/Audio/AudioPipe.cs:170-181` |
| Removal | x asks "Remove {name}? Programs on it go back to the master; apps using its cable hear nothing." | `VolumePageViewModel.Inputs.cs:94-100` |

**What the user has to do today:** Wiring tab > "+ Combine devices" > type a name > click the "Microphones" chip > tick both
microphones > Create. The result is a card under "Combined devices" and a row in the Mixer's Microphones group. Other programs
(Discord, Teams, OBS) must then pick **"CABLE Output (VB-Audio Virtual Cable)"** as their microphone; the name the user typed is
never seen by those programs (Windows' endpoint name is the driver's).

**Cable budget on this PC (mixer.json: `SoundMode = "full"`, `BusCable = CABLE Input`, Helium and Spotify with effects on).**
VB-CABLE gives one pair: two playback endpoints, "CABLE Input" and "CABLE In 16ch", but only one recording side, "CABLE Output"
(HANDOVER.md 2520, 2541; `tests/OmniControl.Tests/OwnerPc.cs:15-18`). `VirtualCables.Find` pairs by name, so "CABLE In 16ch" gets no
recording side (`Core/Engine/Audio/VirtualCables.cs:33-35`) and is not `Recordable`. In Full control the master bus always takes a
cable before new combined devices (`RoutePlanner.cs:120` before `:122`) and takes "CABLE" first (name order). So:

- a new combined microphone is **always blocked "Needs another virtual cable"** on this PC in Full control; the window says so in one
  line, then Create still succeeds and the card shows the caution tag. Usable recordable pairs for microphones: **zero**.
- In **Fast** mode the cable pair is idle (the bus does not run, programs use Capture and replay), but the planner blocks every
  combined device there by rule (`RoutePlanner.cs:108`), not because something is missing.

**Two latent faults found** (not caused by this request, they matter for it):

1. *Remembered cables outrank the bus.* Remembered combined/microphone cables are taken before the bus (`RoutePlanner.cs:115-116`).
   If the bus is ever given up (`BusGivenUp`), a waiting combined microphone takes "CABLE" and remembers it; from then on it keeps it
   and the bus falls back to "CABLE In 16ch" (`TakeAny()` with no filter, `:120`).
2. *"CABLE In 16ch" feeds the same "CABLE Output".* In case 1, everything the bus carries (all PC sound) would also come out of
   "CABLE Output", i.e. into the combined microphone that apps record. The planner treats the two inputs as two independent cables.

## 2. Why it is unclear to the owner

1. **The Microphones group has no combine action.** Its rows show "Records from: This microphone", fixed words with no menu
   (`VolumePageViewModel.Wiring.cs:13, 85-89`), while the Outputs rows right above have chips and "+ add". Nothing in the place the
   owner is looking says microphones can be combined.
2. **The entry point is generic and far away.** "+ Combine devices" sits in the top bar beside the sound mode; the window opens on
   Speakers, and "Microphones" is a small chip the user has to notice and click (`CombineDeviceViewModel.cs:48`).
3. **The empty state explains nothing.** "Combined devices / 0 devices / None yet"; the only hint is a tooltip
   (`VolumePage.axaml:63-64`). It does not say what a combined microphone is for or that apps then use "CABLE Output".
4. **Combined microphones live in the wrong group in Wiring.** They are cards under "Combined devices", mixed with speakers, not
   rows under Microphones; the Wiring Microphones table only takes plain `InputItem`s (`VolumePageViewModel.Wiring.cs:37`). In the
   Mixer they do appear under Microphones, so the two tabs disagree about where they belong.
5. **The blocker is learned last.** On this PC the only result is "Needs another virtual cable" + "More cables", which opens
   VB-Audio's page; nothing says that the cable is held by the master bus or that Fast mode would leave it free.
6. **The name the user types is not the name apps show.** Nothing says "apps pick CABLE Output".
7. **No per-microphone level, no hint about Stereo Mix** (it records what the PC plays: an echo source in calls).

## 3. What Windows allows without a custom driver

- **Mixing N capture streams into one virtual microphone: yes,** exactly what `MicRun` does: shared-mode WASAPI capture of each
  member, mix, chain, render into the cable's playback side; apps record the cable's recording side. No driver of our own, no admin.
- **The app-visible name cannot be ours.** Renaming "CABLE Output" means writing the endpoint's friendly-name property (admin,
  system state): out of scope; the UI must name "CABLE Output" plainly.
- **Clock drift: not handled on the capture side today.** Each member runs on its own clock; the mix runs on the cable's render
  clock. `AudioPipe.Fill` only trims a ring past 120 ms back to 20 ms (an audible jump) or re-primes 20 ms of silence when it runs dry
  (`AudioPipe.cs:177-179`; `CoreTiming.cs:26`). At ~100 ppm that is a click every few minutes per microphone. The playback side
  already has the right tool: `ExtraOutput` reads its queue through a `WdlResampler` nudged by queue fill
  (`PipeOutputs.cs:16-82`, `CoreTiming.cs:51-52`). **Reuse it** by moving that logic into one shared "drift reader" used by both
  `ExtraOutput` and each capture ring (rule 13).
- **Format and rate:** captures ask for 48 kHz float stereo, then 16-bit (`PipeCapture.cs:47-51`). On this PC every mix format is
  48 kHz / 2 ch / float (HANDOVER.md 2541). Mono or 16 kHz headsets rely on Windows' shared-mode conversion: unverified here.
- **Per-source gain and mute:** cheap, a per-ring gain in `Fill` (a `float` per source written atomically, no lock).
- **Monitoring ("hear yourself"):** `AudioPipe` already plays one mix on several devices with drift correction, so a monitor
  output is one more entry in `RunSpec.Outputs`. It adds feedback risk with speakers (see below); keep it out of the first cut.
- **Echo and feedback:** Stereo Mix records the Realtek speaker's output, which in Full control is the master bus output (all PC
  sound). In a call the other party hears themselves; with monitoring on speakers it loops. A combined microphone must never be a
  member of itself and must never record a cable (already true: cables are filtered out, `VolumePageViewModel.Combine.cs:47-48`).
- **Single VB-CABLE pair:** one recordable consumer at a time. More pairs need VB-CABLE A+B / C+D (separate donationware, cannot be
  bundled, HANDOVER.md 284) or another cable driver.

## 4. Design options

Shared by all options (do these whatever is picked):

- **S1. Allow combined microphones in Fast mode.** A combined microphone needs only a recordable cable, not the bus or Equalizer
  APO. In Fast the planner gives recordable cables to combined microphones (remembered first), and the gate for combined
  *microphones* becomes Open in Fast. Combined speakers stay Full only (unchanged). On this PC that makes the feature work today.
- **S2. One pair is one cable.** Core: inputs of one driver that share one recording side ("CABLE Input" and "CABLE In 16ch")
  are one `VirtualCable` with extra playback ids, or at least a planner rule: when a recording consumer holds a pair, none of the
  pair's inputs go to anyone else. Fixes fault 2.
- **S3. The bus keeps its place.** Remembered recording cables never take the pair the bus needs while the bus runs; the microphone
  then says "Needs another virtual cable" and offers the step (fault 1).
- **S4. Drift-corrected captures** (shared drift reader, section 3).
- **S5. Per-member level and mute** stored on the combined microphone, applied in the mix.
- **S6. Honest status words** (calm, one sentence each, principle 11), defined once in `SoundModes`:
  - running: "Apps record it from CABLE Output" (the engine's running text, unchanged in meaning)
  - "Needs another virtual cable" (tooltip: "The master bus uses CABLE. In Fast mode it is free.") + link "Switch to Fast"
  - "Needs VB-CABLE" + pill "Install VB-CABLE"
  - "None of its microphones is plugged in" (unchanged)
  - member tag on Stereo Mix: `Border.tag.caution` "Records the PC's sound" (tooltip: "In calls others may hear themselves.")

### Option A (recommended): combined microphones are Microphones rows, edited like Outputs

The Microphones table gets the same chip editing the Outputs table already has; a combined microphone is a row there, not a card.

- **Entry point:** a `Button.pill` "+ Combine microphones" on the Microphones group header (the `GroupHeader` tool slot, like the
  Outputs group tools), plus on every real microphone's "Records from" cell a `Button.scope` with chevron whose `FlyoutPresenter.listmenu`
  has `Button.menurow` "Combine with ..." entries (one per other microphone) and existing combined microphones ("Add to Desk mics").
- **Naming:** creating opens the existing Combine devices window pre-set to Microphones (kind chips hidden, first microphone already
  ticked when started from a row). Default name "Combined microphone" (numbered when taken), selected for typing. Rename later via
  Edit (same window).
- **Sources:** the new row's "Records from" cell shows `Button.chipx` chips (one per member, x takes it away) and
  `Button.blockadd.small` "+ add", exactly like a program's "Plays on" cell; the + menu lists recording devices not yet in it.
- **Per-source level/mute:** clicking a chip's words opens a small `FlyoutPresenter.listmenu`: the member's name
  (`TextBlock.menutext`), one `Slider.compact` (0-200 %, default 100, double-click resets via `SliderReset`, wheel via `ValueWheel`),
  and a `Button.pill` "Mute" toggled with `.custom`. Mouse first (rule 5).
- **Result:** the row's Source cell = mic glyph + name + `Border.tag` "Combined"; Cable cell = `CableView` (cable name, or the
  caution tag with "More cables" / "Install VB-CABLE" / `Button.link.small` "Switch to Fast"). The Mixer row is unchanged.
- **"Combined devices" group:** keeps combined *speakers* only (and is renamed "Combined speakers" when only speakers can be there).
- **Unplugged member:** chip reads "Headset (not plugged in)" (existing `ChipLabelOf` pattern), the mix runs on with the others
  (short restart gap); none left: status "None of its microphones is plugged in".
- **Effects:** the combined row's effects link opens its FX window as today; chain runs after the mix. Each member's own effects
  are not in the mix (the row records the device itself): tooltip on the chip "Its own effects are not included".
- **Remove:** the row's x (existing ask: "Remove Desk mics? Apps using CABLE Output hear nothing.").

**Visual spec A (Volume > Wiring, Microphones group)**

```
Header:  [mic glyph] Microphones   2 devices                         [+ Combine microphones]   (pill, right)
Columns: Source | Records from | Cable
Row 1:   Stereo Mix (Realtek(R) A...   [This microphone v]            —
Row 2:   Microphone Array (AMD ...     [This microphone v]            —
Menu of Row 2 (listmenu):  title "Combine with"
         - Stereo Mix (Realtek(R) Audio)        [tag caution: Records the PC's sound]
         - (when one exists) Add to Desk mics   [tag: Combined]
```
States of the combined row:
- *Empty (just created from the header, nothing ticked):* not possible: Create needs two ticks (unchanged `MinMembers = 2`).
- *One mic ticked (window open from a row):* window line "Tick at least one more microphone"; Create dimmed.
- *Ready:* `Desk mics [Combined] | [Microphone Array x] [Stereo Mix x] [+ add] | CABLE` (tooltip "Apps record it from CABLE Output").
- *Blocked, Full control, bus holds the cable:* Cable cell `[Needs another virtual cable]` (caution tag) + `Switch to Fast` (link) +
  `More cables` (pill). Tooltip: "The master bus uses CABLE. In Fast mode it is free."
- *Blocked, no VB-CABLE:* `[Needs VB-CABLE]` + `Install VB-CABLE` (pill, `.main`).
- *Member unplugged:* chip "Headset (not plugged in)", row keeps running on the rest.

### Option B: keep the window, make it say what it does (smallest change)

- **Entry point:** "+ Combine devices" stays in the top bar; add the same window behind a `Button.link.small` "Combine
  microphones" on the Microphones group header, which opens it on Microphones.
- **Window additions:** a one-line `TextBlock.hint` under the kind chips: Microphones: "Mixes them into one microphone. Apps record
  it from CABLE Output."; per ticked member a `Slider.compact` level on the right of its `OptionList` entry; the Stereo Mix caution
  tag; the cable line names the holder: "CABLE is used by the master bus. Switch to Fast to free it." with `Button.link.small`.
- **Result:** card stays under "Combined devices", but the group's empty state becomes two words plus a tooltip:
  "None yet" -> "None yet. Combine speakers or microphones." (tooltip "A combined microphone appears to apps as CABLE Output.").
- **Unplugged / effects / remove:** as today.
- Per-member levels are edited only in the window (Edit).

**Visual spec B (Combine devices window, 460 px, Microphones)**

```
Title:    Combine devices
Name:     [Desk mics                      ]
Kind:     (Speakers) (Microphones*)              chips, Microphones selected
Hint:     Mixes them into one microphone. Apps record it from CABLE Output.
List:     [x] Microphone Array (AMD Audio Device)      ----o---- 100 %
          [ ] Stereo Mix (Realtek(R) Audio)  [Records the PC's sound]
Cable:    CABLE is ready for it.                                         (ready)
          CABLE is used by the master bus. Switch to Fast to free it.  [More cables]   (blocked, Full)
          Needs VB-CABLE.                                        [Install VB-CABLE]   (none)
Buttons:  [Create] [Cancel]          Create dimmed until a name and two ticks
```
States: empty (no ticks: "Tick at least two devices", Create dimmed); one ticked (same, the ticked row shows its slider); ready
(Create enabled, cable line "CABLE is ready for it."); blocked (as above, Create still allowed, the card then waits).

### Option C: one "OmniControl microphone" with send switches (a bus, not a device list)

- A single fixed row at the top of Microphones: "OmniControl microphone" (= the recordable cable), always present when a recordable
  cable exists. Every real microphone row gets a `ToggleSwitch` "Send to OmniControl microphone" in its Records-from cell and, when on,
  a `Slider.compact` send level. Effects live on the OmniControl microphone row.
- No naming, no window, no member lists: it matches the single-pair reality of this PC (one virtual microphone at most).
- Several combinations at once are impossible by design; a second cable pair would need a second fixed row ("OmniControl microphone
  2"), which scales badly. Breaks the existing `CombinedDevice` model for microphones (migration of any stored combined microphone
  into sends).

**Visual spec C**

```
Header:  Microphones   3 devices
Row 0:   OmniControl microphone [Virtual]  | Mixes: Microphone Array, Stereo Mix | CABLE   (or caution tag + Switch to Fast)
Row 1:   Stereo Mix (Realtek...)           | [switch] Send  ---o--- 100 %  [Records the PC's sound] | —
Row 2:   Microphone Array (AMD ...)        | [switch] Send  ---o--- 100 %                           | —
```
States: empty (no sends on: row 0 says "Turn on Send on a microphone"); one send on (runs: one microphone through the cable, which
is what a processed microphone already does); ready (two sends); blocked (row 0's cable cell caution tag + Switch to Fast / More
cables / Install VB-CABLE).

### Comparison and recommendation

| | A: rows + chips | B: better window | C: send switches |
|---|---|---|---|
| Discoverable from the Microphones group | yes (header pill and row menu) | yes (header link) | yes (switch on every row) |
| One way to edit (rule 11) | mirrors Outputs' Plays-on chips | keeps a separate card style | new pattern |
| Per-source level | chip pop-down | window only | inline slider |
| Effort | medium | small | medium, plus migration |
| Scales to more cable pairs | yes | yes | poorly |

**Recommendation: Option A plus S1-S6.** It puts combined microphones where the owner looks, edits them exactly like Outputs, keeps
the existing window for naming (one combine path), and with S1 it works on this PC today in Fast mode. B is the fallback if a
small change is wanted first; its window wording also feeds A.

## 5. Data model, planner and engine changes (Option A + shared)

**Data (`App/Audio`):**
- `CombinedDevice`: add `Dictionary<string,int> MemberLevel` (absent = 100) and `HashSet<string> MemberMuted`, so `Members` stays
  a `List<string>` and old `mixer.json` files load unchanged (no migration; a `List<CombinedMember>` would need one).
  Limits (0-200, default 100) defined once next to the other level limits (`Core/Models/Percent.cs` holds percent maths).
- `MixerService.Routing.cs`: `SetMemberLevel(key, memberId, level, muted)` (saves, raises `ProgramChanged` like `SetCombinedLevel`).

**Core (`Core/Engine/Audio`):**
- `VirtualCables`: group inputs that share a recording side (same driver in parentheses, one recording side found) into one
  `VirtualCable` with `ExtraPlaybackIds`, or expose a `PairOf(playbackId)`; the one place that knows this (S2).
- New `DriftReader` (from `ExtraOutput.Produce`): ring + resampler + fill tracking; `ExtraOutput` uses it; `AudioPipe.Fill` reads
  each capture ring through one (S4). Timings stay in `CoreTiming`.
- `AudioPipe.SetSourceGain(int index, float gain)`: a volatile float per ring applied in `Fill` (S5).

**Planner (`RoutePlanner.cs`):**
- Fast branch: hand recordable cables to combined microphones (remembered first) and plan them with `PlanMic`; speakers keep
  `CombinedNeedsFull` (S1).
- Full branch: remembered recording cables are taken after the bus when the bus runs (or: the bus's remembered `BusCable` is taken
  first), and taking a pair takes all its inputs (S2, S3).
- New blocked reason text from S6, held as constants in `SoundModes`.

**Engine:** `MicRun.ApplyLevel` also pushes member gains (`OnProgramChanged`); no other change. Restart on member change is today's
behaviour (spec changes -> restart, short gap).

**Gate:** `SoundFeature.CombineDevices` split into `CombineSpeakers` (Full only) and `CombineMicrophones` (Open in both modes; the
row's status carries the cable state). `ComboItem.EffectsGate` uses the microphone feature for microphones
(`VolumePageViewModel.Routing.cs:69`).

**Pages:**
- `VolumePageViewModel.Wiring.cs`: `SyncWiring` builds Microphones rows from `InputItem` and microphone `ComboItem`s; combined
  microphone rows use `ShowChips` from `TargetsOf` (members) with a members menu; real microphones get the "Combine with" menu;
  combined speaker cards stay.
- `VolumePageViewModel.Combine.cs`: `CombineAsync(kind, firstMember)` overload so the header pill and row menu open the window
  pre-set; the cable line names the holder (bus) and offers Switch to Fast.
- `CombineDeviceViewModel`: `FixedKind` (hides the chips), pre-ticked member, kind-specific hint.
- Member pop-down: one small view-model (`MemberLevelMenu`) + view; uses `Slider.compact`, `Button.pill`, `TextBlock.menutext`.
- Search: the "Combine devices" card keeps its entry; add keywords "combine microphones mix mics" on the same entry (one list).
- Hotkeys (rule 4): "Mute combined microphone" and "Combined microphone level up/down" with a device source over combined
  microphones (`HotkeyRegistry.Register`, existing pattern in `VolumePageViewModel.Hotkeys.cs`).

## 6. Test plan

**Headless (`tests/OmniControl.Tests`, using `OwnerPc`):**
1. Fast, owner's PC, one combined microphone (both mics): plan has a `Mic` run on `CableInput` with both sources; nothing else uses
   a cable; Helium/Spotify plans unchanged.
2. Full, owner's PC, combined microphone: blocked "Needs another virtual cable"; bus on `CableInput`; no run uses `Cable16` for the
   bus while the microphone holds the pair (S2).
3. Full, bus given up, then recovered: the bus gets its pair back; the microphone says it needs another cable (S3).
4. Member unplugged: run keeps the other source; both unplugged: "None of its microphones is plugged in".
5. A combined microphone never lists a cable or itself as a member (window lists, menu lists).
6. `VirtualCables.Find` on `OwnerPc`: one pair with two playback ids, recording side `CableOutput`.
7. `DriftReader` (pure, synthetic): a ring fed 0.5 % fast/slow stays within the target band for 10 minutes of simulated blocks,
   with no trim and no dry gap.
8. Old `mixer.json` with a combined microphone and no levels loads; levels default to 100.

**By the owner (not by agents, rule 7):** Fast mode, combine both microphones, Discord picks "CABLE Output", speak and play a
video: the other side hears voice and video sound (Stereo Mix); then mute Stereo Mix's chip; unplug and re-plug a USB headset if
one exists; 30-minute call for clicks (drift); switch to Full control and read the row's status; both themes; 720 px window.

## 7. Work breakdown (Option A + S1-S6)

| # | Work | Files | Effort |
|---|---|---|---|
| 1 | Pair grouping (S2) + tests | `Core/Engine/Audio/VirtualCables.cs`, `RoutePlanner.cs` (CableBox), tests | S |
| 2 | Planner: Fast combined microphones, bus first (S1, S3), gate split | `RoutePlanner.cs`, `SoundModes.cs`, tests | M |
| 3 | Drift reader shared by outputs and captures (S4) + test | `Core/Engine/Audio/PipeOutputs.cs`, new `DriftReader.cs`, `AudioPipe.cs`, `CoreTiming.cs` | M |
| 4 | Member levels (S5) | `CombinedDevice.cs`, `MixerService.Routing.cs`, `AudioPipe.cs`, `MicRun.cs` | S |
| 5 | Wiring: combined microphones as Microphones rows, chips, "Combine with" menu, header pill | `VolumePageViewModel.Wiring.cs`, `WiringRow.cs`, `WiringTable.axaml`, `VolumePage.axaml`, `VolumePageViewModel.Combine.cs` | M-L |
| 6 | Window pre-set kind, hint, cable holder line | `CombineDeviceViewModel.cs`, `CombineWindow.axaml` | S |
| 7 | Member level pop-down | new `Pages/Volume/MemberLevelMenu.cs` + view | S |
| 8 | Wording constants, Stereo Mix tag (one list of loopback-type recording names in Core, like `VirtualCables.Drivers`) | `SoundModes.cs`, Core | S |
| 9 | Hotkeys, search keywords, docs (`ARCHITECTURE.md`, `HANDOVER.md`) | `VolumePageViewModel.Hotkeys.cs`, `VolumePageViewModel.cs` | S |

Order: 1, 2 (they fix real faults and make it work in Fast), then 3, 4, then 5-9. One agent per two or three steps; 5 must not run
in parallel with anything touching the Wiring files.

## 8. What the owner must decide

1. Option A, B or C (recommended A).
2. Allow combined microphones in Fast mode (S1)? Recommended yes: it is the only way they run on this PC with one VB-CABLE pair.
3. In Full control with one pair: should the master bus always win the cable (recommended), or may the owner give it to the
   combined microphone (master then plays without effects)?
4. Stereo Mix as a member: allowed with a caution tag (recommended), or not offered?
5. Monitoring ("hear the combined microphone on a device"): leave for later (recommended), or include now?
6. Per-member level range: 0-200 % (recommended) or 0-100 %.

## 9. Not verified

Nothing was run, played, heard or seen. Unverified: that `MicRun` has ever run with two sources on real hardware; Windows'
shared-mode conversion of mono or non-48 kHz microphones in `PipeCapture`; how much the AMD array and Realtek clocks drift; whether
"CABLE In 16ch" really leaks into "CABLE Output" in practice (the VB-CABLE design says it does; HANDOVER.md 2531 marks it
unverified); call apps' echo cancellation with Stereo Mix in the mix; whether the owner's screenshot showed "+ Combine devices"
enabled (mixer.json says Full control, so the code says it is); the exact Wiring layout of option A at 720 px.
