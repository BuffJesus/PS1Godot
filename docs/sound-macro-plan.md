# Sound macro conversion plan — Monitor scene SPU budget

Phase 5 Stage B (commit pending) shipped the SoundMacroSequencer runtime
+ exporter + Lua dispatch. This doc proposes which existing hand-baked
SFX in the Monitor scene to convert into macros + a shared primitive
bank, plus the SPU savings each conversion buys.

Driving constraint: scene 3 last exported at **307628 B SPU vs 262144 B
cap** — over budget by ~45 KB. Macro conversion alone clears the
overrun and leaves headroom.

## Asset audit

Three categories. `Macroable` = composite SFX whose components are
distinct events; `Primitive` = atomic transient that may serve as a
macro event source; `Keep` = drone/loop/textural — macros can't help.

| Clip                | ADPCM B | Route | Cat. | Notes |
|---------------------|---------|-------|------|-------|
| ambient_drone       | 18928   | XA    | Keep | streamed; not on SPU |
| shift_end_stinger   | 37824   | XA    | Keep | streamed |
| alarm_distant       | 37824   | XA    | Keep | streamed |
| footsteps_slow      | 18928   | XA    | Keep | streamed |
| hum_low             | 12624   | SPU   | Keep | looped drone |
| hum_fluor           | 12624   | SPU   | Keep | looped drone |
| hum_outside         | 12624   | SPU   | Keep | looped drone |
| hum_electrical      | 12624   | SPU   | Keep | looped drone |
| breathing_low       | 12624   | SPU   | Keep | textural sustain |
| chair_roll          |  9344   | SPU   | Keep | continuous noise |
| door_creak          | 12624   | SPU   | Keep | continuous slow envelope |
| light_dim           | 12624   | SPU   | Keep | electrical buzz fade |
| crt_click           |  6064   | SPU   | Macro | click + pop layer |
| crt_static_short    |  6064   | SPU   | Macro | noise bursts; share static primitive |
| crt_die             | 12624   | SPU   | Macro | pop + whine + thud |
| log_confirm         |  6064   | SPU   | Macro | UI tone (small win) |
| menu_move           |  6064   | SPU   | Macro | UI blip (small win) |
| menu_select         |  7584   | SPU   | Macro | UI tone (small win) |
| box_crash           | 12624   | SPU   | Macro | thud + splinter + debris |
| box_slide           |  6320   | SPU   | Macro | whoosh + thud |
| bulb_pop            |  6064   | SPU   | Macro | electrical pop + glass |
| metal_clang         | 18672   | SPU   | Macro | strike + ring + decay |
| thud                |  6064   | SPU   | Primitive | promote to shared |
| wood_creak          |  9344   | SPU   | Primitive | promote (use in macros) |
| shadow_whisper      |  6320   | SPU   | Macro | whisper layered + jittered |
| splash_chime        |  6320   | SPU   | Keep | already minimal, looped |

Total SPU spend on Macroable + Primitive rows: **127 KB** of 191 KB
SPU-resident SFX. Whittling that down is where the budget comes back.

## Shared primitive bank

These are the atomic transients each macro draws from. All 11025 Hz
mono, kept as short as the envelope allows. ADPCM target = 56% of
PCM, so 0.5 s clip ≈ 3 KB.

| Primitive            | ~PCM | ~ADPCM | Used by macros |
|----------------------|------|--------|----------------|
| `wood_thud_short`    | 0.45 s | 2.8 KB | crash_box, thud, wood_creak_short, box_slide |
| `wood_splinter`      | 0.40 s | 2.5 KB | crash_box |
| `electrical_pop`     | 0.30 s | 1.9 KB | bulb_pop, crt_die, crt_click, crt_static |
| `glass_tinkle`       | 0.40 s | 2.5 KB | bulb_pop |
| `metal_strike`       | 0.40 s | 2.5 KB | metal_impact, crt_die accent |
| `metal_ring_short`   | 0.55 s | 3.5 KB | metal_impact (pitch-shift for echoes) |
| `debris_small`       | 0.35 s | 2.2 KB | crash_box, bulb_pop, generic accents |
| `whine_descend`      | 0.50 s | 3.1 KB | crt_die |
| `air_whoosh_short`   | 0.40 s | 2.5 KB | box_slide, optional door |
| `noise_burst_short`  | 0.30 s | 1.9 KB | crt_static, crt_click |
| `whisper_short`      | 0.45 s | 2.8 KB | shadow_whisper variations |
| `ui_blip_high`       | 0.20 s | 1.3 KB | menu_move, menu_select |
| `ui_blip_low`        | 0.20 s | 1.3 KB | menu_select, log_confirm |

Total primitive bank: **~31 KB** ADPCM. Generated synthetically via
`tools/synth_psx_chime.py`-style additive/FM synth scripts, NOT
re-recorded — keeps reproducibility + tunability.

## Macro definitions

Each macro = `SPLASHPACKSoundMacroRecord` (24 B header) + N event
records (8 B each). Frame ticks at 30 FPS. Pitch offsets in semitones.
Volume 0–128, pan 0–127.

### `macro_crash_wooden_box` → replaces `box_crash`
```
@frame  primitive          vol  pan  pitch  why
0       wood_thud_short    128  64    0     primary impact
0       wood_splinter      100  60   -2     bark crack layer (slight L)
2       debris_small        80  68    0     bouncing chunk
5       debris_small        70  60    3     second chunk, higher pitch
9       wood_thud_short     50  64   -7     soft settle thump
```
12624 B → 0 B (plus shared primitives). **Saves 12.6 KB.**

### `macro_metal_impact` → replaces `metal_clang`
```
@frame  primitive          vol  pan  pitch  why
0       metal_strike       128  64    0     main strike
0       metal_ring_short   100  64    0     ring layer
8       metal_ring_short    70  64   -3     echo, lower
20      metal_ring_short    45  64   -7     fading echo, even lower
```
18672 B → 0 B. **Saves 18.7 KB.**

### `macro_crt_shutdown` → replaces `crt_die`
```
@frame  primitive          vol  pan  pitch
0       electrical_pop     120  64    0
1       whine_descend      110  64    0
6       wood_thud_short     60  64   -8
```
12624 B → 0 B. **Saves 12.6 KB.**

### `macro_bulb_pop` → replaces `bulb_pop`
```
@frame  primitive          vol  pan  pitch
0       electrical_pop     128  64    2
1       glass_tinkle       110  72    0
3       debris_small        70  56    5
```
6064 B → 0 B. **Saves 6.1 KB.** (Smaller absolute, but stronger
character — the 3 components feel more alive than baked composite.)

### `macro_crt_click` → replaces `crt_click`
```
@frame  primitive          vol  pan  pitch
0       electrical_pop      90  64    8     bright pop
1       noise_burst_short   60  64    3     short static
```
6064 B → 0 B. **Saves 6.1 KB.**

### `macro_crt_static_short` → replaces `crt_static_short`
```
@frame  primitive          vol  pan  pitch
0       noise_burst_short  100  64    0
4       noise_burst_short   90  60    2     spatial flicker
8       noise_burst_short   80  68   -2
```
6064 B → 0 B. **Saves 6.1 KB.**

### `macro_box_slide` → replaces `box_slide`
```
@frame  primitive          vol  pan  pitch
0       air_whoosh_short    90  64   -3
8       wood_thud_short     70  64   -8     final settle
```
6320 B → 0 B. **Saves 6.3 KB.**

### `macro_thud` → replaces `thud`
```
@frame  primitive          vol  pan  pitch
0       wood_thud_short    128  64    0
```
Trivial — but it normalises the dispatch path so callers don't need
to special-case "is this a macro or sample?" 6064 B → 0 B.
**Saves 6.1 KB.**

### `macro_wood_creak_short` → replaces `wood_creak`
```
@frame  primitive          vol  pan  pitch
0       wood_thud_short     60  60  -12
12      wood_thud_short     50  68  -10
24      wood_thud_short     40  64   -8
```
9344 B → 0 B. **Saves 9.3 KB.** (A creak is essentially a slow
sequence of micro-thuds at descending pitches — fits the macro
pattern naturally.)

### `family_shadow_whisper` → replaces `shadow_whisper`
SoundFamily, not macro. One primitive `whisper_short`, runtime picks
random pitch (-3..+3) / volume (96..120) / pan jitter ±20 per
dispatch — every play sounds slightly different, which matters for
horror atmosphere where repeated triggers stand out.
6320 B → 0 B. **Saves 6.3 KB.**

### UI tones (`menu_move`, `menu_select`, `log_confirm`)
Macros built from `ui_blip_high` + `ui_blip_low` primitives:
```
macro_menu_move:   ui_blip_high@0(pitch+5)
macro_menu_select: ui_blip_low@0, ui_blip_high@2(pitch+8)
macro_log_confirm: ui_blip_low@0(pitch+3), ui_blip_high@4(pitch+10)
```
6064 + 7584 + 6064 = 19712 B → 0 B. **Saves 19.7 KB.**

## Total budget impact

Removed sample data (per scene-3 export):
```
crt_click          6064
crt_static_short   6064
crt_die           12624
log_confirm        6064
menu_move          6064  (menu scene; reuses primitives)
menu_select        7584  (menu scene)
box_crash         12624
box_slide          6320
bulb_pop           6064
metal_clang       18672
thud               6064
wood_creak         9344
shadow_whisper     6320
                 ─────
                 109 KB removed
```
Add primitive bank: **+31 KB**.
Add macro+event records: ~10 macros × 24 B + ~30 events × 8 B = **~480 B**
(negligible).

**Net savings ≈ 78 KB SPU** for scene 3 + carries into menu scene.
Scene 3 goes from 307 KB (over by 45) → ~229 KB (under by 33).

## Implementation order

Sequenced to deliver budget headroom early and validate the
generation pipeline before committing to many macros.

1. **Generator script** `tools/synth_sfx_primitives.py`
   - Mirrors `synth_psx_chime.py`. Outputs the 13 primitives to
     `assets/audio/sfx_primitives/*.wav`.
   - Synth approach per-primitive: short additive partials + noise
     burst + envelope shape. Documented per-function the way
     `synth_psx_chime.py` documents bell/sub/pad.
   - Reproducible. Tweak knobs for pitch/decay/noise mix per
     primitive without re-recording.

2. **Wire primitives into PS1Scene.AudioClips**
   - Drop each primitive PS1AudioClip resource into monitor scene
     (and any others using SFX). Auto-route stays SPU.
   - Sanity export: confirm primitives ship ≈31 KB ADPCM total.

3. **Author macros — high-impact first**
   - `macro_metal_impact` (saves 18.7 KB)
   - `macro_crt_shutdown` (12.6 KB)
   - `macro_crash_wooden_box` (12.6 KB)
   - `macro_wood_creak_short` (9.3 KB)
   - Verify each via Sound.PlayMacro from a test Lua hook before
     deleting the original WAV.

4. **Migrate Lua callers**
   - Search Lua for `Audio.PlaySfx("metal_clang")` etc., replace
     with `Sound.PlayMacro("macro_metal_impact")`.
   - Anti-spam already handled by macro CooldownFrames / MaxVoices
     authored on the resource.

5. **Drop the original samples**
   - Once Lua callers are migrated and macros verified, remove the
     hand-baked WAVs from PS1Scene.AudioClips. Re-export, confirm
     SPU number drops to ~229 KB.

6. **Repeat for remaining macros + UI tones + family_shadow_whisper.**

## Out of scope

- Looped drones (hums, breathing) — macro events are one-shots; SPU
  loops are sample-level only. These stay as samples.
- XA-routed clips (ambients, stingers, footsteps) — already free of
  SPU pressure; no macro work warranted.
- The intro-splash music chime (`splash_chime`) — already optimal at
  6.3 KB and runs only at boot; conversion saves <1 KB net.

## Risks

- **Primitive synth fidelity.** Synthetic primitives are tunable but
  may sound thinner than recorded equivalents. Mitigation: layer 2–3
  primitives per macro for thickness; the variation jitter helps
  more than absolute timbre quality.
- **Voice budget under heavy macro layering.** A macro firing 4
  events simultaneously costs 4 SPU voices for the duration of the
  longest event. Phase 4 voice allocator handles eviction at
  Priority order, but author-set Priority on each macro matters —
  default 64 (DEFAULT_SFX_PRIORITY) leaves room for music + dialog.
- **Lua call site churn.** Every `Audio.PlaySfx("box_crash")` becomes
  `Sound.PlayMacro("macro_crash_wooden_box")`. Mass-replace with care
  — call sites in `lua/monitor/feed_manager.lua` (33 KB) likely
  hold most of them.
