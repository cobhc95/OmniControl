# FX window header: total response graph and live analysis (owner-approved spec)

Mockup of the resting state: design-a-fx-header.html (same folder). Study it and match it closely (sizes in Avalonia layout units).

## Resting header (one row, same height as today, nothing else moves)
- Left: the existing Effects on/off switch and "Effects on" label (unchanged).
- Right end of the same row: three live numbers (label muted, value primary, weight 500): `Peak` (dB), `Latency` (ms), `CPU` (% of one core). NO init time (owner removed it).
- Far right: the thumbnail graph, 150 x 34, 0.5 px border, 6 px radius, the TOTAL response curve of the whole chain (always the total; no total/each-effect switch), log frequency 20 Hz-20 kHz, 0 dB line, accent-coloured 1.5 px curve, small scale label (e.g. ±12) top right.
- Narrow window: the three numbers fold into a tooltip on the graph; only the graph stays.

## Extreme values (design 3, auto scale)
- Default scale is ±12 dB. When the total goes outside it, the scale widens just enough to show the whole curve (peak plus a small margin, up to about ±27 for ±24 dB bands), label updates ("±27"); it returns to ±12 when the extreme goes away. Cuts are drawn the same way downward. The 0 dB line stays in the middle.
- The highest point gets a small amber dot (warning tone from the palette, not red). The Peak number turns amber (caution) above +12 dB.
- Hover/tooltip text on the graph or the Peak number, e.g. "Boost of 24 dB at about 1 kHz. The level is lowered by 24 dB automatically so it cannot clip." (the automatic headroom already exists in the engine and the APO writer; say what really happens per engine).

## Hover enlarge
- Pointer over the thumbnail for about 300 ms (named timing in Timing.cs) opens a larger graph as a floating panel (popup/overlay, about 440 px wide, graph about 420 x 170) over the panes: it never moves or resizes anything underneath. Closes when the pointer leaves the thumbnail and the panel. Clicking the thumbnail pins it open until clicked again or Esc. The thumbnail is also a keyboard stop (Enter opens, Esc closes).
- Content: full frequency axis (20, 50, 100, 200, 500, 1k, 2k, 5k, 10k, 20k) and dB axis with the same auto scale; grid lines; the TOTAL curve only (no Total/Each-effect buttons); an amber peak marker; a vertical crosshair that follows the pointer in frequency with a small label "1.00 kHz / +24.0 dB" (the response value at that frequency); a channel L/R switch (only if the response can differ per channel; otherwise omit); the same three numbers below with "Peak +24.0 dB at 1.00 kHz".
- Note under the graph: it shows only the linear effects (the equalisers and gain; IR loader if its response is known); compressors, limiters, VST3 and other level-dependent or unknown effects are not drawn ("Shows EQ and gain only").

## Live metrics (all real-time)
- Graph and Peak: recomputed from the chain's current settings on every edit (cheap; use the same biquad coefficients as the engine/EqMath; include every enabled band of every enabled linear effect; include band types, the graphic EQs (32/16 bands) if present, Gain, and the effects' wet/bypass state); peak is the maximum of the total curve over a fine log grid, with its frequency.
- Latency: live sum of the reported latency of the effects that actually run (VST3 reported latency in samples converted to ms, plus any engine latency of the row's method: capture/replay or cable path), "none added" for Equalizer APO rows.
- CPU: measured live in OmniControl's own engine as processing time per audio block divided by the block's real-time duration, smoothed (about one update per 500 ms; define in Timing.cs), shown as % of one core. When the row runs through Equalizer APO (master/microphones in Fast mode) OmniControl does not process the sound: show "not measured here" for CPU and "none added" for Latency, the graph still shows what was sent to Equalizer APO. When effects are off or the engine is not running show "-".
- Everything honest per AGENTS.md; no number is faked.
