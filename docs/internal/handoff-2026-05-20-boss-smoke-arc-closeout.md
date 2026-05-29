# Handoff — boss_smoke arc closeout + combat framework planning (2026-05-20)

Picked up from `handoff-2026-05-19-boss-smoke-debug.md` (4 open
bugs, 14 uncommitted files). Landed the 14 uncommitted edits as
7 per-area commits, then ran the encounter on real hardware
(PCSX-Redux with keyboard input) and hit **7 more bugs** in
sequence — each one revealed the next. All 11 are now fixed and
committed. The session also produced the framework RFC, a
user-facing tutorial that should make the *next* boss painless,
two PS1Doctor lint checks (first two from the RFC's L5 set),
and a blind math fix for the deferred Bug #3 camera-pitch.

**HEAD:** `c0619d6`. Working tree clean. Not pushed.

## What got fixed and shipped

Eleven bugs, in commit order:

| # | Commit | Bug | Root cause |
|---|---|---|---|
| 1 | `b77c487` | HP bar appeared on frame 1 | Brain script owned visibility; should be the fog gate's job |
| 2 | `faf323c` | HUD fill rect buried under BG | OT LIFO inserted children in author order → BG drawn last (on top); reversed inner loop |
| 5 | `3fdab82` | Boss self-damaged from own swing | OverlapBox at player position; boss in attack range was inside its own swing AABB. Added `~= self` filter |
| 7 | `7a6f0dc` | Boss never moved, never out of range | `dx*dx` via FP.__mul rescales /4096; distSq landed in fp12 not fp12², 4096× too small vs *_RADIUS_SQ. Switched to `dx._raw * dx._raw + dz._raw * dz._raw` |
| 6 | `823a5fb` | Boss aggro'd from frame 1 | No encounter gate. Player spawn was inside aggro radius. Persist flag flipped by fog gate, brain returns early until set |
| 8 | `29f5c94` | Boss swings whiffed silently | Player avatar had no PS1HurtBox — `OverlapBoxDetailed` only returned the boss itself, no damage anywhere |
| 9 | `0edaa90` | Player walked back out of arena | No `onTriggerExit` handler. Added snap-back with hardcoded `TRIGGER_Z_RAW=-2048` (runtime doesn't pass trigger entity to Lua, only the index) |
| 10 | `7f7e732` (paired) | Boss camped at attack-range edge | AGGRO branch never decremented `RECOVER_FRAMES` timer. Made AGGRO chase while `stateTimer > 0 OR out_of_range` |
| 11 | `7f7e732` (paired) | Boss had infinite swing reach | fireAttack AABB anchored on player position; box teleported to wherever target stood. Re-anchored on boss with ±2 PSX extent |

Two non-bug demo improvements landed alongside:

- `421b92e` controls overlay + manual lock-on toggle (no
  controller-required auto-lock; X-press dismiss overlay)
- `da3c8ed` translucent backing box on the controls overlay
  (couldn't ship in `421b92e` because the Box-fill render bug #2
  was still open)

## What was learned — encoded in three new docs

The eleven bugs revealed that the existing combat primitives
work but the *composition* is full of subtle traps. Three docs
shipped:

### `docs/internal/rfc/combat-framework.md` (be7cae8)

Five-layer plan for compressing the encounter pattern into a
framework. Reads as "given each of the 11 bugs, what framework
surface would have prevented it?" Layers:

- **L1 Combat Lua module** — DistanceSqRaw, MeleeSwing,
  ChaseStep, MeleeBoss state machine. Closes Bugs #5/#7/#10/#11
  by construction.
- **L2 Encounter Lua module** — fog gate lifecycle. Closes
  Bugs #1/#6/#9.
- **L3 UI.Bar module** — stat-bar update helpers. Closes the
  repetitive HUD update code.
- **L4 Composite Godot nodes** — `PS1Encounter`, `PS1StatBar`
  for designers who don't write Lua. Lower into L1–L3 at export.
- **L5 PS1Doctor lints** — 9 checks (PS1Stats without
  HurtBox, fill exceeds BG, etc.) that would have caught most
  of these bugs at editor time.

Phased: L1+L3+Doctor first, L2 next, composite nodes deferred
until a second boss exists to extract from, BossBT graph kind in
its own future RFC. **Migration plan uses boss_smoke as the
validation case** — brain expected to shrink ~200 → ~25 lines.

### `docs/authoring/boss-encounters.md` (ab8b666)

User-facing recipe. 600 lines walking through scene authoring
(6 nodes) + the 4 Lua scripts + an 8-point runtime acceptance
checklist. Every code block includes the inline comments that
explain the rules baked in — so authors copying from the doc
inherit the survival rules instead of re-deriving them. Added
to mkdocs nav under Authoring.

### `combat-patterns.md` extended (be7cae8)

Added 80-line "Foot-guns observed in production" section near
the bottom listing all 11 gotchas with workarounds. Today's
authors have a survival guide until the framework lands.

## Runtime quality-of-life: diag noise gated (81c3580)

The per-frame `[PadDiag]`, `[FRC]`, `[CollideDiag]` prints that
landed during the debug arc were spamming every TTY session.
Wrapped each behind `#ifdef PSXSPLASH_VERBOSE`. Default builds
are now clean; pass `-DPSXSPLASH_VERBOSE` to re-enable.

`[StatsDiag]` and the `ColliderDiag` table dumps stay
unconditional — they're one-shot at scene load with high signal-
to-noise.

## Autonomous work after the initial handoff was written

Three commits landed after the user stepped away to test the
controller. None require controller verification beyond what's
explicitly noted.

### `1cd625b` `CombatValidationReport` — Doctor lint #1: Stats without HurtBox

First entry in the RFC's L5 set. Symmetric to the existing
texture/audio/animation/decal validators — emits warnings into
the shared `validatorOffenders` sink that the PS1 Doctor dock
surfaces alongside other reports. Would have caught Bug #8 at
editor time (player avatar had PS1Stats but no PS1HurtBox →
combat looked broken because OverlapBoxDetailed silently ignored
the player).

### `c0619d6` Doctor lint #2: HurtBox without Stats

Symmetric check. Author who adds a PS1HurtBox to an entity that
has no PS1Stats has dead-weight authoring — the hurtbox shows up
in hits but Stats.DealDamage no-ops on it. Dedups across multiple
hurtboxes per entity (head/body/legs is common) so the offender
list gets one row per entity, not per hurtbox.

dotnet build verified clean for both lints (0 errors, 6 pre-
existing warnings unchanged).

### `8a922cc` Bug #3 camera pitch math (BLIND — pending controller verify)

Applied the math sketched in the 2026-05-19 handoff: pitch
(`playerRotationX`) rotates `(Y, Z)` of the rig offset before
the yaw rotates `(X, Z')`. Camera now arcs around the player's
head when pitching instead of pivoting the view in place. Pitch
clamp at `controls.cpp:249` is already `±π/2` so no clamp added.

**Verify when you have a controller.** Right-stick Y to pitch
up/down. Expect: camera physically arcs over/under the player,
maintaining roughly constant distance, view stays centered on
the avatar. If signs feel inverted (e.g. pitching up moves
camera down), the fix is to negate `sinX` in the two
`pitchedY/Z` lines — single-line revert.

## What's still open

### Awaiting controller verification

OBDX Pro VX arrives May 22-25 per memory `project_obdx_eta`.
Until then the user is on keyboard, and these two need a pad.

- **Bug #3 — Camera pitch orbital arc.** Math fix is *committed*
  (`8a922cc`); the user can't drive right-stick Y from keyboard
  so verification waits. On F5 with controller: pitch should
  arc the camera around the player, not tilt the view in place.
  If something's off, see the rollback note in the commit
  description.
- **Bug #4 — Framerate dips.** User reports performance drops
  during gameplay. Total geometry budget (188 tris) is well
  within range so it's probably not raw fillrate. Hypotheses
  listed in the original handoff (Lua flood was the cost; UV
  scroll on fog wall; subdivided floor). Needs
  `PSXSPLASH_PERFOVERLAY` (already on in current build)
  breakdown of GTE/CPU/Lua time during dips. Controller
  needed to actually move around and reproduce.

### Next-session candidates (controller-not-required)

1. **Combat framework Phase 1 implementation** (1–2 days). Per
   the RFC, ship:
   - `godot-ps1/lua/lib/combat.lua` — `DistanceSqRaw`,
     `InRange`, `MeleeSwing`, `ChaseStep`. Pure Lua helpers.
   - `godot-ps1/lua/lib/ui_bar.lua` — `UpdateStatBar`,
     `BindStatBars`. Pure Lua.
   - **Loading mechanism is the only blocker.** Options:
     (a) C++ Lua API registration that loads embedded Lua
     source at runtime startup (globally available);
     (b) per-scene init script using `onSceneCreationStart`
     (runs before entity onCreates — verified
     `scenemanager.cpp:597` vs `:606`);
     (c) per-script `require`-equivalent (psxlua may not
     support it; not investigated).
     Lean toward (a) for "no per-scene plumbing." Path (b)
     ships faster if (a) hits scope creep. **Pick before
     starting Phase 1** — both code paths diverge from there.
   - Migrate `boss_smoke_player.lua`'s `updateBars` to
     `UI.UpdateStatBar` as the first caller. Verify behavior
     unchanged on keyboard-driven F5.
2. **More PS1Doctor lint checks** (cheap per-check, no
   architecture lift). RFC § L5 lists 9 candidates; 2 shipped
   (`1cd625b`, `c0619d6`). Next four with highest signal:
   - `Bar fill exceeds BG` (geometric sanity).
   - `Encounter without boss` (broken `PS1Encounter` wiring —
     waits on the composite node existing).
   - `Trigger position not authored` (auto-compute
     `TRIGGER_Z_RAW` from AABB at export, so authors don't have
     to hardcode it).
   - `Boss attack range > arena extent` (designer hint that
     the boss will never miss).
3. **Combat module Phase 2** (1 day, after Phase 1 ships):
   `Combat.MeleeBoss` state machine. This is where the brain
   shrinks ~200 → ~25 lines.

### Lower-priority polish

- **Trigger callback API** — runtime currently passes only the
  trigger *index* to Lua, not a GameObject handle. Adding the
  handle as the first arg (and the index as second) would let
  authors do `Entity.GetPosition(self)` instead of hardcoding
  trigger positions. Small breaking change for scripts that
  already treat `self` as a number; backward-compat is doable.
- **`Persist.Get` nil semantics** — returning nil for unset
  keys is a foot-gun when authors concatenate or do arithmetic
  on the result. Could either default to 0 in the API or add a
  `Persist.GetOrDefault(key, default)` variant. Lean toward the
  latter — current `nil ~= 1` semantics are fine for the
  common branch-check case, but `or 0` should always work for
  the concat case (which it does).
- **Player hurtbox auto-add** — when a `PS1Stats` resource is
  attached to a `PS1MeshInstance` and there's no `PS1HurtBox`
  child, the doctor warning + a "Quick action: add default body
  hurtbox" would close the Bug #8 loop entirely. Per the RFC.

## Uncommitted state

None. Working tree is clean (except this handoff edit, which
will be committed in the same step that updates it).

```
$ git -C /d/Documents/JetBrains/PS1Godot status --short --untracked-files=no
(empty)
```

## Notable user feedback this session

- **"Figure it out instead of guessing. Debug or something."**
  Mid-session, after I'd done a few "try this, see if it works"
  fixes that didn't fully resolve the issue. The right move when
  this fires: add Lua / runtime diag prints with a version
  marker (`v2`, `v3`, etc.) so build cache hits show up as a
  missing marker, and the *data* tells us state instead of my
  hypothesis chain. Worked the first time I applied it — the
  user's diag output revealed boss was in state 2 (TELL)
  cycling, not stuck in state 1 (AGGRO), which redirected my
  hypothesis from "chase logic broken" to "boss only does
  one chase frame per cycle." Saved several wrong fixes.
- **"Boss attacks me everywhere I move."** Pointed at Bug #11
  (attack box anchored on player). I'd briefly dismissed this
  as "you're inside attack range, that's intended" — wrong. The
  user pushed back: "I was at the edge of the floor." That's
  what made me look at fireAttack's AABB construction and find
  the player-anchored box.

Both interactions reinforced: **trust the user's direct
observation over my model of the code.** When a symptom doesn't
match expected behavior, the model is wrong, not the user.

## Memory updates landed this session

None. All session-specific (boss debugging, framework planning)
either landed in committed code/docs or in the
`combat-framework.md` RFC. Nothing rises to "future Claude
session needs to know this in their context" beyond what's
already in the existing memories.

## Suggested opener for next session

> "HEAD `c0619d6` (or later after this handoff commit). boss_smoke
> arc closed — 11 bugs fixed; framework RFC, tutorial, and
> foot-gun reference docs landed; 2 of 9 Doctor lint checks
> shipped; Bug #3 camera pitch math applied blind, waits on
> controller. Framework RFC at
> `docs/internal/rfc/combat-framework.md`; Phase 1 is the
> next big lift but blocked on the Lua-lib-loading design
> decision (C++ registration vs per-scene init vs require).
> What next — pick a loading mechanism and ship Phase 1, more
> Doctor lints (cheap), or the deferred controller-required
> Bugs #3/#4 (OBDX ETA May 22-25)?"

## 2026-05-29 increment — 2 more Doctor lints shipped

Picked up the "more Doctor lints" path from the suggested
opener. Two checks landed in `60970a6`, both targeting the
hand-authored `*_bg` / `*_fill` UI-element pairs from the
boss_smoke demo (which the deferred PS1StatBar composite
node will eventually emit identically — heuristic stays
load-bearing across both authoring paths):

- **RFC §L5 row 3 — Bar fill exceeds BG** (Warning). Per-canvas
  pair lookup by name suffix; AABB containment check. Fires when
  the fill rect escapes the bg rect on any side.
- **RFC §L5 row 9 — Paired bars both near-black** (Info). Same
  pairing infra; warns when both colors have RGB sum < 32 (PSX
  framebuffer loses sub-step contrast — black-on-black is dead).

Both produce 0 warnings on the existing demo (hp_fill inset 2px
inside hp_bg; bg color sum 60 is above the near-black threshold).
dotnet build clean (0 errors, 6 pre-existing warnings unchanged).
**4 of 9 RFC §L5 checks now shipped** (1cd625b, c0619d6, plus the
two in 60970a6).

Remaining cheap L5 candidates **all wait on the deferred
PS1Encounter / PS1StatBar composite nodes**:

- Stats without HUD — needs PS1StatBar to enumerate stat-bar refs
- Encounter without boss — needs PS1Encounter.BossEntity
- Encounter ID collision — needs PS1Encounter
- Boss attack range > arena — needs PS1Encounter (or Lua parse)
- Boss missing PS1Lua — needs PS1Encounter
- Trigger position not authored — needs PS1Encounter

So the next sensible move is **the loading-mechanism decision for
Combat Lua modules** (Phase 1 of the framework RFC). After that,
shipping `combat.lua` + `ui_bar.lua` unblocks the boss_smoke
migration which is the proof case the RFC sequenced its plan
around. Composite nodes (Phase 4) are still gated on a *second*
boss existing to extract their shape from, per the RFC.

HEAD as of this addendum: `60970a6`. Working tree otherwise
matches the prior handoff state.

## 2026-05-29 increment — Combat framework Phase 1 shipped

Phase 1 of the combat framework (`docs/internal/rfc/combat-framework.md`
§L1 + §L3) landed in `74074b6`. The loading-mechanism
question is resolved in favor of option (a) — embed the Lua
source in psxsplash and pcall it at runtime startup — for three
reasons evident from reading the runtime:

1. **Precedent.** `FSM`, `Quest`, and `BT` already ship this way
   in `psxsplash-main/src/lua.cpp:Lua::Init`.
2. **Cross-script reads already work.** Per-script envs are
   constructed in `LoadLuaFile` with `__index = _G` as a
   fallback metatable (`lua.cpp:633-641`). A global installed
   once is readable from every script; *writes* still silo per
   memory `project_psxlua_per_script_env`.
3. **Author can't forget to ship the lib** — it's in the binary.

Wiring detail worth remembering: `Lua::Init()` runs from
`L.Reset()` which fires *before* `LuaAPI::RegisterAll`
(`scenemanager.cpp:73 vs :86`). At that point `_G.UI` doesn't
yet exist, so a `UI.UpdateStatBar = ...` write would create an
empty `UI` table that RegisterAll then clobbers with
`L.setGlobal("UI")`. Solution: new `Lua::InstallCombatLibrary()`
method called from scenemanager.cpp:88, after RegisterAll has
populated the engine globals.

### Surface shipped

| Helper | Resolves |
|---|---|
| `Combat.DistanceSqRaw(a, b)` | Bug #7 — FixedPoint.__mul's /4096 rescale |
| `Combat.InRange(a, b, units)` | Sugar over DistanceSqRaw |
| `Combat.MeleeSwing{...}` | Bugs #5 (skip_self) + #11 (anchor on attacker) by default. y_below/y_above override symmetric range for asymmetric silhouettes (boss_smoke 1+2). |
| `Combat.ChaseStep{...}` | Wraps `(d * speed) / 4096`, skips y |
| `UI.UpdateStatBar{...}` | Width/height default to authored via UI.GetSize |

`Combat.MeleeBoss` state machine is Phase 2 (deferred — it's
where the brain shrinks ~200 → ~25 lines, so it lands as one
big migration diff once it exists).

### First caller migrated

`boss_smoke_player.lua` updateBars: 15 lines → 5. Behavior
identical (width/height read from the authored hp_fill /
stamina_fill = 100×4). F5 verification expected unchanged
bar tracking; `boss_smoke_brain.lua` not migrated (waits on
Phase 2).

### Build note

psxsplash header (`lua.h`) gained a public method, so the
build was `make clean && make` per the stale-.o trap memory.
New `psxsplash.ps-exe` 342016 bytes; F5 on boss_smoke needs
the Run-on-PSX path to pick this up (it should automatically
since the build uses the same install path).

### Next-session candidates (refreshed)

1. **F5 verify the migration.** Run boss_smoke, confirm HP +
   stamina bars track identically to pre-migration. If they
   don't, the `UI.GetSize` default-width fallback is the most
   likely suspect — pass explicit `width = 100, height = 4` in
   the player script.
2. **Combat framework Phase 2** — `Combat.MeleeBoss` state
   machine (1 day per the RFC). Migrate
   `boss_smoke_brain.lua` to it; the diff is the proof case.
3. **Combat framework Phase 3** — `Encounter` module (1 day).
   Closes Bugs #1/#6/#9 by construction. After Phase 2 ships.
4. **Doctor lints once composite nodes exist.** 6 of the
   remaining RFC §L5 candidates wait on Phase 4 nodes.
5. **Deferred controller-required Bugs #3/#4** — OBDX hardware
   memory says ETA May 22-25 (now overdue). If the pad has
   arrived, Bug #3 camera pitch needs right-stick verification
   and Bug #4 framerate dip needs PSXSPLASH_PERFOVERLAY
   readings under actual movement.

HEAD as of this addendum: `74074b6`.

## 2026-05-29 increment — Combat framework Phase 2 shipped

`Combat.MeleeBoss` state machine landed in `c58cb90`. The
boss_smoke brain migration is the proof case: **237 → 81
lines**, of which ~30 are actual code (the rest is comments
and the authoring-surface table literal). The hand-rolled
state machine plus five fix-comment paragraphs the original
carried for Bugs #5/#7/#10/#11 are all gone — those rules
live inside the library now where the next boss author
inherits them by default.

### Design choices worth remembering

- **Five states (not six).** The original brain had an
  explicit `STATE_PHASE2`; the framework folds it into a
  generic phase-override mechanism (`phases` is an array,
  `phase.hp_ratio` is the entry threshold, `phase.<key>`
  shadows `def.<key>` via `effective(key)`). Souls bosses
  with three/four phases are now array-extensions, not new
  states.
- **Phase index is monotonic.** Once entered, never reversed
  — souls bosses don't un-phase on heal.
- **Pure mechanics + opt-in feel.** State machine fires no
  shakes/pauses without an explicit callback. boss_smoke
  provides `on_tell`/`on_hit_land`/`on_death`/`on_enter`. A
  silent boss is the default.
- **Death cleanup is split.** Infrastructure (hide HP canvas,
  set persist key, deactivate entity) is declarative via def
  fields. Cinematic (shake, pause, Camera.LockOff) is the
  `on_death` callback. `on_death` runs *first* so it can
  read live state.
- **Two iframe knobs.** `iframes` per-hit (boss_smoke=6),
  optional `iframes_phase_change` for the longer invuln
  during the phase-2 transition shake (boss_smoke=60).

### What the migration loses

The migrated brain doesn't preserve one thing from the
original: the boss_smoke `STATE_PHASE2` was explicitly a
*faster chase* (`step = 4096 / 20` vs phase 1's `4096 / 32`).
The framework's `chase_speed_fp12` defaults to 128 ≈ 4096/32
across all phases. To restore the speed-up, add
`chase_speed_fp12 = 205` (≈4096/20) to the phase 2 override
table — leaving the base at the 32-frame cadence. Not done
yet pending F5 verification of whether phase 2 needed it for
difficulty or it was just original-encounter polish. Noting
here so we don't lose track.

### Open from Phase 1 (still open)

F5-verify the player updateBars migration on boss_smoke. The
Phase 2 commit didn't touch the player bar path, but the same
F5 will exercise both Phase 1 and Phase 2 at once — start
the encounter, watch bars + boss behavior. Look for:
- HP/stamina bars track identically (Phase 1).
- Boss IDLE → AGGRO → TELL → HIT → AGGRO → ... → PHASE_2
  on hitting 50% HP, with shorter tell + bigger shake.
- Death: shake + pause + HP canvas hides + boss deactivates.

### Next-session candidates (refreshed)

1. **F5 verify the migrations** (Phase 1 + Phase 2 together).
2. **Combat framework Phase 3** — `Encounter` module
   (~1 day per RFC). Collapses the `Persist.Get("..._aggro")`
   gate, music start, HP canvas reveal, fog-gate retreat
   block into `Encounter.new{...}`. boss_smoke_fog_gate.lua
   migration is the proof: ~75 → ~10 lines per the RFC.
3. **Combat framework Phase 4** — composite Godot nodes
   (`PS1Encounter` + `PS1StatBar`). Gated on a *second*
   boss existing per the RFC's "compose, don't speculate"
   guidance. Also unblocks the remaining 5 RFC §L5 Doctor
   lints.
4. **Deferred controller-required Bugs #3/#4** — OBDX
   overdue (ETA was May 22-25 per `project_obdx_eta`).

HEAD as of this addendum: `c58cb90`.
