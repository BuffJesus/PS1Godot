# Handoff — boss_smoke arc closeout + combat framework planning (2026-05-20)

Picked up from `handoff-2026-05-19-boss-smoke-debug.md` (4 open
bugs, 14 uncommitted files). Landed the 14 uncommitted edits as
7 per-area commits, then ran the encounter on real hardware
(PCSX-Redux with keyboard input) and hit **7 more bugs** in
sequence — each one revealed the next. All 11 are now fixed and
committed. The session also produced the framework RFC and a
user-facing tutorial that should make the *next* boss painless.

**HEAD:** `ab8b666`. Everything pushed.

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

## What's still open

### From the original handoff (controller-required)

Both deferred — neither is testable without a pad, and the user
is on keyboard until at least the OBDX Pro VX arrives (memory:
`project_obdx_eta` says May 22-25).

- **Bug #3 — Camera pitch orbital arc.** Right-stick Y pitches
  the camera but rotates the world around a fixed point instead
  of arcing the camera over/under the player's head.
  Fix sketched in the original handoff (apply pitch to (Y, Z)
  of the rig offset before yaw rotation, in
  `scenemanager.cpp:1094-1112`). Math is straightforward; can be
  written blind, but verifying it requires a right-stick.
- **Bug #4 — Framerate dips.** User reports performance drops
  during gameplay. Total geometry budget (188 tris) is well
  within range so it's probably not raw fillrate. Hypotheses
  listed in the original handoff (Lua flood was the cost; UV
  scroll on fog wall; subdivided floor). Needs `PSXSPLASH_PERFOVERLAY`
  (already on in current build) breakdown of GTE/CPU/Lua time
  during dips. Controller needed to actually move around and
  reproduce.

### New, controller-not-required (next-session candidates)

1. **Combat framework Phase 1 implementation** (1–2 days). Per
   the RFC, ship:
   - `godot-ps1/lua/lib/combat.lua` — `DistanceSqRaw`,
     `InRange`, `MeleeSwing`, `ChaseStep`. Pure Lua helpers.
   - `godot-ps1/lua/lib/ui_bar.lua` — `UpdateStatBar`,
     `BindStatBars`. Pure Lua.
   - Loading mechanism — biggest design decision. Options:
     (a) C++ Lua API registration that runs Lua source at
     startup; (b) per-scene init script using
     `onSceneCreationStart`; (c) per-script `require`-equivalent
     (psxlua may not support it). Lean toward (a) for global
     availability without per-scene plumbing, but (b) is the
     quickest to ship if (a) hits scope creep.
   - Migrate `boss_smoke_player.lua`'s `updateBars` to
     `UI.UpdateStatBar` as the first caller. Verify behavior
     unchanged.
2. **PS1Doctor lint checks** (alongside Phase 1). Per the RFC,
   start with the four highest-signal:
   - `Stats without HurtBox` (would have caught Bug #8 at
     editor time).
   - `Bar fill exceeds BG` (geometric sanity).
   - `Encounter without boss` (broken `PS1Encounter` wiring).
   - `Trigger position not authored` (auto-compute
     `TRIGGER_Z_RAW` from AABB at export, so authors don't have
     to hardcode it).
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

None. Working tree is clean.

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

> "HEAD `ab8b666`. boss_smoke arc closed — 11 bugs fixed, three
> docs landed (RFC, tutorial, foot-gun reference). Framework
> RFC is `docs/internal/rfc/combat-framework.md`; Phase 1 is
> the next big lift but blocked on the Lua-lib-loading design
> decision (C++ registration vs per-scene init vs require).
> What next — framework Phase 1, the deferred camera pitch /
> framerate bugs (needs controller; OBDX ETA May 22-25), or
> something fresh?"
