# Handoff — F5-verify the combat framework stack (2026-05-29)

This is an **interactive verification handoff**. The 2026-05-29
session shipped the entire combat-framework + Phase 5 BossBT
RFCs in 10 feat commits, but none of it has been F5-verified
on hardware/emulator — the user was away from their setup.

**Protocol for the next-session Claude:** walk the user through
the questions below ONE AT A TIME. Ask the question, then STOP
and wait for the user's answer before asking the next one. Do
not batch questions. Do not proceed past a failing answer
without diagnosing.

Each step lists:
- **Ask:** the exact question to put to the user.
- **Expect:** what a passing answer looks like.
- **If it fails:** triage notes for what to check / fix.

When all steps pass: congratulate the user, append a new
addendum to `handoff-2026-05-20-boss-smoke-arc-closeout.md`
marking the framework verified, and remove this file (or
move it to `archive/`).

## Background to share if the user asks

The 2026-05-29 session shipped the combat-framework RFC end-to-end:

| # | Slice | Commit |
|---|---|---|
| 1 | Phase 1 — Combat + UI.UpdateStatBar | `74074b6` |
| 2 | Phase 2 — Combat.MeleeBoss | `c58cb90` |
| 3 | Phase 3 — Encounter module | `111a480` |
| 4 | Phase 4-A — PS1Encounter composite node | `e5e0510` |
| 5 | Phase 4-B — 4 encounter Doctor lints | `285b243` |
| 6 | Phase 4.5 — PS1StatBar + L5 #9 lint | `3e49577` |
| 7 | L3 v2 — UI.BindStatBars auto-tick | `7afc244` |
| 8 | Phase 5-A — BossBT compiler | `23f8586` |
| 9 | Phase 5-B — BossBT editor UI | `d5251f4` |
| 10 | Migration — boss_smoke_brain → BossBT graph | `008259b` |

HEAD = `fdfee0d` (the last docs commit), pushed to origin/main.

boss_smoke shrunk: brain 237→23 lines (-90%), fog_gate
72→0 (deleted), 6 hand-rolled UIElements → 3 PS1StatBars,
player.lua updateBars boilerplate removed, brain config moved
into a BossBT graph.

Full chain: inspector authoring → visual graph → Lua compile
→ engine ticks. No hand-rolled Lua boilerplate left in the
demo. This verification proves it works.

---

## Step 1 — Editor startup health

**Ask:**

> Open the PS1Godot project in Godot. Let it scan for a few
> seconds — it needs to generate `.uid` files for the new
> `PS1Encounter.cs` and `PS1StatBar.cs`, and rebuild the C#
> assembly with all of today's changes.
>
> When the editor settles:
> 1. Any red error popups about missing scripts, broken
>    resources, or compilation failures?
> 2. Does the Output panel at the bottom show anything like
>    `[PS1Godot]`, `script error`, `failed to load`, or
>    `Parse error`?
>
> If both are clean, just say "clean." Otherwise paste the
> message verbatim.

**Expect:** clean. Maybe `[PS1Godot]` first-run setup messages
(harmless). Possibly a one-time `Editor: regenerating UIDs`
flurry.

**If it fails:**
- `script error` for PS1Encounter/PS1StatBar → C# assembly
  didn't rebuild. Have the user `dotnet build` from
  `godot-ps1/` then restart the editor.
- `failed to load` on boss_smoke.tscn → likely the new
  `ExtResource("13_statbar")` failed to resolve. Check that
  `godot-ps1/addons/ps1godot/nodes/PS1StatBar.cs.uid` was
  auto-generated.
- `Parse error` → corruption somewhere. Worth `git diff
  HEAD~10..HEAD` and reading the user the changed file list.

---

## Step 2 — PS1Encounter inspector

**Ask:**

> Open `demo/boss_smoke/boss_smoke.tscn` in the editor.
> Click the `FogGate` node in the scene tree.
>
> Verify the inspector shows these fields under PS1Encounter
> groups, with these values:
>
> - **Identity → EncounterId:** `smoke_boss`
> - **Volume → HalfExtents:** `(1, 1, 1)`
> - **Boss → BossEntity:** NodePath pointing at `../Boss`
> - **HUD → BossHPCanvas:** NodePath pointing at
>   `../BossHPCanvas`
> - **Audio → MusicTrack:** `boss_theme`, MusicVolume: `100`,
>   SfxOnEnter: `fog_gate_whoosh`
> - **Retreat → BlockRetreat:** ON (checkbox), ArenaAnchor:
>   `(0, 0, 0)`
>
> Are all six groups present with those values? Any
> "configuration warnings" (the yellow triangle on the node
> in the scene tree)?

**Expect:** all six visible with the listed values. No
configuration warnings.

**If it fails:**
- Inspector shows raw `script = ...` instead of grouped
  fields → script didn't bind. Editor restart needed; UID
  may not have generated.
- BossEntity / BossHPCanvas NodePaths empty → the .tscn
  edit didn't take. `git diff HEAD -- demo/boss_smoke/boss_smoke.tscn`
  to confirm.
- Configuration warning about `BossHPCanvas '..' is not a
  PS1UICanvas` → the NodePath resolves but to the wrong
  node type. Check that BossHPCanvas still has the
  PS1UICanvas script attached.

---

## Step 3 — PS1StatBar inspector

**Ask:**

> Still in `boss_smoke.tscn`. Click these three nodes
> one at a time:
>
> 1. `BossHPCanvas/BossHPBar`
> 2. `PlayerHPCanvas/PlayerHPBar`
> 3. `PlayerHPCanvas/PlayerStaminaBar`
>
> Each should show PS1StatBar inspector groups
> (Identity, Geometry, Appearance, Label, Binding). Verify:
>
> - **BossHPBar:** ElementName=`boss_hp`, X=20, Y=20,
>   W=280, H=4, Padding=4, FillColor reddish, BGColor near-
>   black, TrackedEntity = `../../Boss`, TrackedStat=`hp`.
> - **PlayerHPBar:** ElementName=`hp`, X=18, Y=202, W=100,
>   H=4, Padding=2, FillColor greenish, TrackedEntity =
>   `../../Player/Avatar`, TrackedStat=`hp`.
> - **PlayerStaminaBar:** ElementName=`stamina`, X=18,
>   Y=214, W=100, H=4, Padding=2, FillColor yellowish,
>   TrackedEntity = `../../Player/Avatar`, TrackedStat=
>   `stamina`.
>
> All three bind correctly? Any configuration warnings?

**Expect:** all three correctly populated. No warnings.

**If it fails:**
- TrackedEntity empty → the .tscn migration didn't take,
  same fix as Step 2.
- Configuration warning "must be a direct child of a
  PS1UICanvas" → unlikely given the .tscn layout but
  check the parent.
- Wrong TrackedStat enum → check the .tscn for the
  `TrackedStat = "..."` line; should be the lowercase
  stat key (`hp` / `stamina` / `mana`).

---

## Step 4 — BossBT graph editor

**Ask:**

> Open the PS1 Graph dock (right-hand side, may be in a
> tab next to FileSystem). Load
> `demo/scripts/boss_smoke_bossbt.tres` via the dock's
> Open button.
>
> Verify:
>
> 1. The Kind dropdown reads `Boss BT (Combat.MeleeBoss
>    config)`.
> 2. Two nodes render on the canvas: `bossbt_config #0`
>    and `bossbt_phase #1`.
> 3. Both have a **muted crimson title bar** (the BossBT
>    category tint).
> 4. `bossbt_config #0` shows 17 LineEdits filled in,
>    starting with `smoke_boss` and ending with `60`. The
>    title prefix shows ⚔.
> 5. `bossbt_phase #1` shows 4 LineEdits starting with
>    `0.5` and ending with `Camera.ShakeRaw(900, 30)`.
>    Title prefix shows ⚠.
>
> All five checks pass?

**Expect:** all five pass.

**If it fails:**
- "Unknown kind 'bossbt_config'" rendered as fallback →
  the editor doesn't know the kind. Check that
  `s_kinds` includes the bossbt entries (commit
  `d5251f4` should have added them).
- Title tint is grey, not crimson → `s_categoryTints`
  missing the `BossBT` entry, or `s_kindMeta` for the
  kinds doesn't have `Category: "BossBT"`.
- Only 13 LineEdits (not 17) → the 008259b migration's
  Phase 5 first slice extension didn't take. Check that
  `BuildVisualBody.bossbt_config` has the slots 13-16
  `EmitBossBtPayloadEdit` calls.

---

## Step 5 — F5 export logs

**Ask:**

> Hit **F5** (or click the Run-on-PSX button in the
> PS1Godot dock).
>
> Watch the Output panel during export. Paste back any
> lines starting with `[PS1Godot]`, `[CombatLint]`, or
> `Error`.
>
> Key things to look for that should PASS:
> - `[PS1Godot] Encounter 'FogGate' (id='smoke_boss')
>   AABB=[...] triggerZRaw=-2048 hpCanvas='boss_hp'
>   luaIdx=N`
> - `[PS1Godot] Auto-recompiled PS1Graph:
>   '...boss_smoke_bossbt.tres' →
>   '...boss_smoke_bossbt.lua'`
> - **Zero `[CombatLint]` ERROR lines.** (Warning lines on
>   the surviving `BG` UIElement vs the PS1StatBar pair
>   might fire — that's fine, the boss's "BOSS" label
>   stays as a separate Text element.)
>
> Does export complete? Game launch?

**Expect:** both expected lines present, no errors, game
launches.

**If it fails:**
- Encounter log missing → PS1Encounter was not detected by
  SceneCollector. Likely script/UID issue; restart editor.
- Auto-recompile log missing → `UserScripts` array doesn't
  include the .lua, or the .tres doesn't exist. Check
  boss_smoke.tscn for the UserScripts line.
- `[CombatLint]` ERROR for `Encounter without boss` →
  BossEntity NodePath unresolved at export time. The
  inspector check in Step 2 should have caught this.
- Compile error in generated `boss_smoke_bossbt.lua` →
  open the .lua and look for a `nil --[[ ... ]]` comment
  naming a bad payload. The number-parser fallback when
  a payload isn't a valid number.

---

## Step 6 — PSX boot

**Ask:**

> The game window (PCSX-Redux or hardware) should now be
> showing the boss_smoke scene.
>
> Verify:
> 1. Scene renders — you can see the floor, fog wall,
>    boss cube somewhere in front of you.
> 2. **Player HP bar (green) + stamina bar (yellow)** are
>    visible at the bottom-left.
> 3. **Boss HP bar is NOT visible** (encounter not yet
>    started — that's correct; the canvas is hidden until
>    you cross the fog gate).
> 4. First-run controls overlay is visible (the "CONTROLS"
>    panel with LEFT STICK / etc.). Press X to dismiss it.
>
> All four correct?

**Expect:** all four pass.

**If it fails:**
- Black screen / no render → splashpack load failed.
  Check PCSX-Redux's console for `Lua error` / `assertion
  failed` messages.
- Player bars not visible → PS1StatBar lowering produced
  wrong UIElements. Check `[PS1Godot]   UICanvas
  'player_hp'` log from export — should show 4 elements
  (2 bg, 2 fill).
- Boss bar visible from frame 1 → BossHPCanvas
  `VisibleOnLoad = false` got lost. Check the .tscn.
- Controls overlay missing → not a framework concern,
  pre-existing behavior. Skip.

---

## Step 7 — Walk to fog gate

**Ask:**

> Walk the player forward toward the fog wall (the
> translucent purple plane).
>
> When the player's AABB crosses the fog wall (~Godot z=2,
> fp12 z=-2048):
>
> 1. **`fog_gate_whoosh` SFX plays** (one-shot).
> 2. **`boss_theme` music starts**.
> 3. **Boss HP bar appears** (red gauge at top of screen
>    with "BOSS" label beneath).
> 4. **Boss starts behaving** — turns to face you, begins
>    chasing if out of attack range.
>
> All four trigger on the cross?

**Expect:** all four fire simultaneously on the AABB cross.

**If it fails:**
- Nothing happens on cross → trigger AABB wrong. The
  PS1TriggerBox lowering uses HalfExtents — if it
  defaulted to (1,1,1) you'd expect a 2×2×2 cube around
  the FogGate origin (Godot 0,1,2). Verify in PCSX-Redux's
  TriggerDiag if enabled.
- SFX fires, music doesn't → `Music.Play` failing
  silently. Audio.PlaySfx and Music.Play take different
  resource types — check the boss_theme clip is registered.
- Music + SFX but no HP bar → `UI.SetCanvasVisible` not
  finding `boss_hp`. Probably a canvas-name mismatch.
- HP bar appears but boss doesn't wake → encounter set
  the `smoke_boss_aggro` flag but boss's MeleeBoss
  gate check isn't seeing it. Check that the brain has
  `encounter_id = "smoke_boss"` (it should — Phase 3
  binding).

---

## Step 8 — Boss state machine

**Ask:**

> Stand in front of the boss without attacking. Watch the
> boss's behavior over ~5 seconds.
>
> Expected loop:
>
> 1. Boss **rotates to face you** (Math.Atan2 yaw).
> 2. If you're out of attack range (>2 world units), boss
>    **chases toward you** — small position steps each
>    frame.
> 3. When in attack range AND its recovery timer is 0,
>    boss enters **TELL state**: small camera shake
>    (Camera.ShakeRaw(82, 4)) fires.
> 4. After ~30 frames (0.5s), boss **swings**: an attack
>    AABB checks for player hits. If you're in range, your
>    HP drops + a bigger shake + brief pause.
> 5. After the hit window, boss returns to chase state for
>    ~30 frames (RECOVER), then can tell again.
>
> Does the chase → tell → hit → recover loop fire as
> described?

**Expect:** all five phases visible in sequence.

**If it fails:**
- Boss never moves → MeleeBoss `update()` not running,
  or chase step zero. Check `Persist.Get("smoke_boss_aggro")`
  is 1 after gate cross.
- Boss moves but tell shake never fires → `on_tell`
  callback didn't compile into the BossBT graph. Read
  the generated `boss_smoke_bossbt.lua` and look for
  `on_tell = function...`.
- Tell fires but no swing damage → `on_hit_land` callback
  or `Combat.MeleeSwing` failing. Check that the player
  Avatar has a PS1HurtBox child (Doctor lint #1 should
  have caught a missing one at export).
- Boss camps at edge of attack range, never tells →
  RECOVER timer not decrementing (the old Bug #10).
  Should be fixed by MeleeBoss's recovery flow.

---

## Step 9 — Hit the boss

**Ask:**

> Press R2 to swing your melee attack. Position yourself
> close to the boss so the swing connects.
>
> Expected on a connecting hit:
>
> 1. **Boss HP bar shrinks** proportionally (the gauge,
>    not the background panel).
> 2. **Camera shake + brief pause** on hit.
> 3. Boss takes the hit and continues its state machine —
>    doesn't aggro-spam or break.
>
> Does the HP bar visually shrink as you land hits?

**Expect:** HP bar tracks live HP via UI.BindStatBars +
UI.TickBars auto-tick (the L3 v2 path).

**If it fails:**
- HP bar doesn't shrink despite damage → the
  bind-then-engine-ticks path failed. Most likely cause:
  `UI.TickBars` not being called. Check `lua.cpp` for
  the `TickFrameworkAutoBindings()` definition and
  `scenemanager.cpp` for the call site (should be right
  before the per-entity onUpdate loop).
- HP shrinks but no shake → `on_hit_land` callback didn't
  bind. Read the generated bossbt .lua for the
  `on_hit_land` function literal.
- Boss takes damage but Stats.GetHP doesn't drop →
  Stats.DealDamage no-op. Boss might be missing its
  PS1Stats resource — Doctor lint should have caught this.

---

## Step 10 — Phase 2 transition

**Ask:**

> Keep attacking the boss until its HP drops **below 50%**.
>
> Expected exactly once when crossing 50%:
>
> 1. **Big camera shake** (`Camera.ShakeRaw(900, 30)` —
>    larger magnitude, longer duration than the per-hit
>    shake).
> 2. **Brief invuln window** — your next swing should
>    bounce off without applying damage (60-frame i-frames
>    per `iframes_phase_change`).
> 3. **Boss tells faster** afterward — TELL state should
>    be ~15 frames (half the phase-1 duration of 30).
>
> Did all three fire?

**Expect:** phase 2 cutscene shake + invuln + faster tells.

**If it fails:**
- No shake at 50% → `phases` array in the compiled bossbt
  .lua not present, or `hp_ratio = 0.5` parsed wrong.
  Check the generated .lua.
- Shake but no invuln → `iframes_phase_change` not
  applied. Could be that the field landed in the wrong
  slot in CompileBossBt (lockstep with payload index 16).
- Tells aren't faster → `tell_frames = 15` phase override
  didn't make it into the table. Check the
  `phases = { { ... } }` block in the generated .lua.

---

## Step 11 — Boss death + cleanup

**Ask:**

> Continue attacking until the boss HP hits 0.
>
> Expected death sequence:
>
> 1. **Large shake** (`Camera.ShakeRaw(1228, 30)`).
> 2. **Long pause** (`Scene.PauseFor(12)` — gameplay
>    freezes for ~12 frames).
> 3. **Camera unlocks** if you were locked on
>    (`Camera.LockOff()`).
> 4. **Boss HP canvas hides** (red bar disappears).
> 5. **Boss deactivates** — boss cube stops rendering /
>    can't be interacted with.
>
> All five fire in order on the killing blow?

**Expect:** clean death sequence per the on_death callback
+ MeleeBoss death infrastructure.

**If it fails:**
- Boss HP bar lingers → either `UI.SetCanvasVisible` in
  MeleeBoss handleDamage didn't fire, or the canvas name
  didn't resolve. Check `def.hp_canvas == "boss_hp"`.
- Boss doesn't deactivate → `Entity.SetActive(entity,
  false)` didn't fire. Check MeleeBoss's death path order.
- Camera doesn't unlock → `on_death` callback didn't run.
  Check the generated bossbt .lua for the `Camera.LockOff()`
  in the `on_death` function.
- Persist flag not set → `def.persist_dead_key` derived
  from `encounter_id = "smoke_boss"` should produce
  `"smoke_boss_dead"`. Step 12 will catch a miss.

---

## Step 12 — Retreat block + post-kill gate open

**Ask:**

> Two sub-tests:
>
> **Sub-test A — block retreat during active fight.** This
> requires resetting the scene (PS1 reset button or F5
> again) and crossing the gate but NOT killing the boss.
> While the boss is alive:
>
> - Walk back through the fog wall toward the spawn side.
> - **Expected:** you should be snapped back into the
>   arena (Z teleport to ArenaAnchor=0) + small camera
>   shake.
>
> **Sub-test B — gate open after kill.** With the boss
> dead (from your previous attempt or a fresh kill):
>
> - Walk back through the fog wall.
> - **Expected:** you walk through freely. No snap-back,
>   no shake.
>
> Both sub-tests behave correctly?

**Expect:** A blocks, B passes through.

**If it fails:**
- A doesn't block → `block_retreat` field didn't make it
  into the compiled encounter Lua. Check the
  `boss_smoke_fog_gate.lua`-equivalent auto-generated
  file (look in the export log for the
  `<auto>/encounter_FogGate.lua` SourcePath).
- A blocks but no shake → `Camera.ShakeRaw(82, 4)` in
  `Encounter.onExit` not firing. Should be the default
  thud — check `lua.cpp` for the Encounter source.
- B blocks too → `Persist.Get("smoke_boss_dead")` check
  in `onExit` not seeing the flag. Could mean the death
  path didn't actually set the flag (Step 11 sub-test).

---

## Step 13 — Player death

**Ask:**

> Reset the scene one more time. This time, **let the
> boss kill you** — stand in attack range and don't
> dodge or attack.
>
> Expected at HP <= 0:
>
> 1. `Debug.Log("player died")` line appears in PCSX-Redux's
>    console.
> 2. Large camera shake.
> 3. Controls become unresponsive (Controls.SetEnabled(false)).
> 4. **Player HP bar freezes at 0** — doesn't continue
>    updating, doesn't disappear.
>
> All four fire?

**Expect:** clean player-death sequence.

**If it fails:**
- Player death log doesn't fire → `onDamage` handler
  not detecting hp <= 0. Check that Stats.DealDamage
  actually returned > 0 (might be hitting i-frames every
  swing).
- HP bar continues updating → UI.TickBars not skipping
  inactive entities. The Entity.IsActive check in
  TickBars is the safety net.
- HP bar disappears entirely → not expected; would mean
  Entity.SetActive(false) for the player. The player
  script doesn't deactivate on death, only disables
  controls.

---

## When all 13 steps pass

1. Push a commit appending to
   `handoff-2026-05-20-boss-smoke-arc-closeout.md` noting
   the verification passed: date, commits-as-of, any
   surprises that came up.
2. Delete this file (or move to `archive/`) — its purpose
   is done.
3. Update the `reference_ps1godot_handoff.md` memory to
   point at the boss_smoke_arc-closeout handoff again
   (currently points here).
4. Suggest to the user that the next moves are either
   (a) the deferred controller-required Bugs #3/#4 if
   OBDX has arrived, or (b) starting a second boss
   encounter to validate the "painless second boss"
   claim.

## When something fails

Don't proceed past a failing step without diagnosing.
Common patterns:

- **Step 1-4 failures** are almost always editor / build
  state: `dotnet build`, editor restart, UID regeneration.
- **Step 5 failures** are export-pipeline: read the
  generated bossbt .lua and the export log carefully.
- **Step 6-13 failures** are runtime: check Lua source
  in psxsplash (`lua.cpp:InstallCombatLibrary` for the
  Combat/Encounter helpers, `scenemanager.cpp:GameTick`
  for the tick hook) and the generated bossbt .lua
  content.

If a fix is needed: make it, commit it (caveman-commit
convention), and re-run the failing step. Don't restart
the whole verification — pick up where you broke.
