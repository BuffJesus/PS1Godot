# RFC — BossBT: PS1Graph kind for visual boss authoring

**Status:** Phase 1 (compiler) shipped 2026-05-29. Editor palette + node-body UI deferred.
**Driver:** The combat-framework RFC (`combat-framework.md`) explicitly
punted "Phase 5 — BossBT graph kind" to a separate future RFC.
This is that RFC, written immediately after L1–L4 + L3 v2 landed.

## What "BossBT" is

A PS1Graph **kind** (`resource.Kind = "bossbt"`) that compiles to a
Lua table consumed by `Combat.MeleeBoss`. Visual authoring layer
**on top of** the existing Lua surface — not a replacement for it,
per the combat-framework RFC's "compose, don't bundle" guidance.

Naming note: "BT" is metaphorical, not literal. A boss config is
not a behavior tree in the BT.new sense (Sequence/Selector/Leaf).
The kind keeps the BT suffix for forward-compat — if a real BT
mode for inter-state transitions ever ships, it would extend
this kind, not replace it.

## What it compiles to

A single Lua table at `_G.bossbt_<basename>`. Drop-in for the
existing Combat.MeleeBoss constructor:

```lua
local boss = Combat.MeleeBoss(_G.bossbt_my_boss)
```

The compiled table shape mirrors what authors write by hand
today (`docs/authoring/boss-encounters.md`):

```lua
_G.bossbt_my_boss = {
    encounter_id = "my_boss",
    aggro_radius = 8,
    attack_radius = 2,
    tell_frames = 30,
    hit_frames = 12,
    recover_frames = 30,
    swing_damage = 18,
    swing_range = 2,
    hp_canvas = "boss_hp",
    hp_element = "boss_hp_fill",
    on_tell = function(self, entity) Camera.ShakeRaw(82, 4) end,
    on_hit_land = function(self, entity, hit, applied)
        if applied > 0 then Camera.ShakeRaw(614, 14) end
    end,
    on_death = function(self, entity)
        Camera.ShakeRaw(1228, 30); Camera.LockOff()
    end,
    phases = {
        { hp_ratio = 0.5, tell_frames = 15, recover_frames = 20,
          on_enter = function(self, entity) Camera.ShakeRaw(900, 30) end },
    },
}
```

Every author who hand-writes `Combat.MeleeBoss{...}` produces a
table of this shape today; BossBT just authors the same table
visually instead of as a Lua table literal.

## Two node Kinds

Conservative scope to start. Two kinds carry the entire config:

### `bossbt_config` (one per graph)

The base configuration. Payload slots:

| Slot | Field | Type |
|------|-------|------|
| 0 | `encounter_id` | string |
| 1 | `aggro_radius` | number |
| 2 | `attack_radius` | number |
| 3 | `tell_frames` | int |
| 4 | `hit_frames` | int |
| 5 | `recover_frames` | int |
| 6 | `swing_damage` | int |
| 7 | `swing_range` | number |
| 8 | `hp_canvas` | string |
| 9 | `hp_element` | string |
| 10 | `on_tell` snippet | Lua statement |
| 11 | `on_hit_land` snippet | Lua statement |
| 12 | `on_death` snippet | Lua statement |

Empty payloads are omitted from the compiled table so
`Combat.MeleeBoss`'s `effective(key)` fallback runs naturally.

Multiple `bossbt_config` nodes in one graph: the compiler keeps
the lowest-Id one and warns about the others. Single root by
construction.

### `bossbt_phase` (zero or more per graph)

A phase override. Payload slots:

| Slot | Field | Type |
|------|-------|------|
| 0 | `hp_ratio` | number 0..1 |
| 1 | `tell_frames` override | int |
| 2 | `recover_frames` override | int |
| 3 | `on_enter` snippet | Lua statement |

Phases sorted by **descending hp_ratio** in the compiled output
— the highest threshold enters first as the boss takes damage.
Souls-style phasing only goes one direction; the runtime
guarantees monotonicity (see `lua.cpp` MeleeBoss).

## What graphs CAN'T express (yet)

These authoring patterns work fine in hand-written
`Combat.MeleeBoss{...}` calls but have no graph-side affordance
in this RFC's scope. Add them additively when a real boss
demands them:

- `swing_y_below` / `swing_y_above` (asymmetric swing AABB).
- `chase_speed_fp12` override per phase.
- `iframes` / `iframes_phase_change`.
- `on_phase_change` global callback.
- Per-phase `swing_damage` / `swing_range` overrides.

For each, adding an extra payload slot + emitting it conditionally
is the only compiler change needed.

## Author flow

1. Create a new PS1Graph resource (`.tres`), set Kind to `"bossbt"`.
2. Add one `bossbt_config` node, fill in the base parameters.
3. Add zero or more `bossbt_phase` nodes, each with `hp_ratio` +
   override fields.
4. Save the resource alongside a sibling `<basename>.lua` file.
   Export auto-recompiles the graph (`RecompileSiblingGraphIfPresent`
   in `SceneCollector.cs`) so the compiled `_G.bossbt_<basename>`
   table ships to the splashpack.
5. From the boss entity's brain script:
   ```lua
   local boss = Combat.MeleeBoss(_G.bossbt_<basename>)
   function onUpdate(self, dt) boss:update(self, dt) end
   function onDamage(self, applied, source) boss:handleDamage(self, applied, source) end
   ```

## Deferred work

### Editor palette + node-body UI (Phase 5 second slice)

`PS1GraphEditorDock.s_kinds` needs entries for `bossbt_config`
and `bossbt_phase` (1-line additions). `s_graphKinds` needs the
`"bossbt"` Kind selectable from New (1-line addition).
**`BuildVisualBody` needs two new cases — roughly 80 lines of
mechanical LineEdit-per-payload code** mirroring the existing
`"state"` and `"bt_leaf"` cases.

This is a substantial chunk of UI code and was deferred from
the Phase 5 first slice for the same reason the combat-framework
RFC deferred Phase 4 composite nodes initially: ship the
mechanics, gate the visual authoring layer on real usage data.
**Hand-authored `.tres` files compile correctly today** — the
PS1GraphNode resource is `[GlobalClass]`, so authors can write
`.tres` payloads in any text editor and the compiler picks them
up. See `docs/internal/examples/sample_bossbt.tres`.

### Phase grouping by exec connections (eventually)

Today's compiler sorts phases by `hp_ratio` descending. A future
slice could read exec edges between phase nodes for explicit
authoring of phase order — useful when two phases share an HP
ratio but should fire in a specific order, or when phases trigger
on non-HP conditions (timer expiry, sub-phase clear). Not in
scope here.

### `bossbt_callback` standalone node

The on_tell / on_hit_land / on_death snippets currently live as
config payloads. A future slice could split them into dedicated
node Kinds connected to the config, matching how FSM authoring
splits per-state callbacks into per-state nodes. Only worth doing
if multi-line callbacks become common — a single string LineEdit
is enough for the boss_smoke-sized snippets today.

## Alternatives considered

- **Skip the kind entirely.** Hand-written
  `Combat.MeleeBoss{...}` works fine and the framework already
  delivers the painless-second-boss target from the original
  RFC. Rejected because the combat-framework RFC's UI/UX
  philosophy memory weights non-intimidating authoring — boss
  visual editing fits the same "designers without Lua" target
  that motivated PS1Encounter / PS1StatBar.
- **Compile to a full BT (Sequence/Selector/Leaf) instead of
  MeleeBoss.** Tempting but rejected: would require duplicating
  the MeleeBoss state machine inside BT nodes, breaking the RFC's
  "compose, don't replace" principle. The MeleeBoss state machine
  is a deliberate distillation of the boss_smoke debug arc's
  11 bug fixes; a generic BT can't carry that knowledge.
- **Single `bossbt_unified` node with all fields including
  phases-as-payloads.** Rejected because phases as inline
  payload slots makes the visual representation degenerate to
  one huge node — defeats the point of the graph kind.

## What ships in the Phase 5 first slice

- `PS1GraphCompiler.CompileBossBt` — new dispatch + emitter.
- `docs/internal/examples/sample_bossbt.tres` — hand-authored
  sample for compiler validation.
- This RFC for posterity.

Editor UI work + a real demo migration land in Phase 5 second
slice on demand.
