# Demo blueprint

A long-term target for what the shipped demo grows into — not a plan to build
all of it now. Each section tags which roadmap bullets it leans on so we can
grow the demo in lockstep with the exporter instead of waiting for everything.

## Why a richer demo

The current demo (floor + 2 cubes + trigger + interactable + audio) is a
pipeline smoke test: prove the export path works end-to-end. Good at that job.
Bad at answering the questions an author actually has before committing to the
toolchain:

- "How do menus work?"
- "Can I switch between 1st and 3rd person?"
- "What do RPG stats / dialog / inventory look like?"
- "How many enemies fit before the framerate drops?"

The shipped demo should answer those without the author reading the roadmap
first. The blueprint below is what a "thorough demo" looks like once the
supporting bullets ship.

## North-star scenes

Scenes numbered by load order. Each is one splashpack today (until chunk
streaming lands in Phase 2.5).

### scene_0 — Start menu
**Requires:** bullet 8 (UI), bullet 6 (audio — already ✓).

- Title card (UI text: "PS1GODOT DEMO") + looping ambient audio clip.
- Menu items: **Start**, **Options**, **Credits**, (later) **Load Save**.
- Input: D-pad up/down moves selection, Cross confirms.
- Selection is Lua-driven: `Input.JustPressed` + `UI.SetColor` on the menu
  items to highlight the focused one.
- Confirming **Start** calls `Scene.Load(1)`.

### scene_1 — Options
**Requires:** bullet 8 (UI), Phase 2.5 save (for persistence).

- Sub-menu of the start menu — same UI system, different canvases.
- **Camera mode** toggle: First-person | Third-person | Orbit. (Third-person
  is the default.) Driven by Lua calling into a new `Camera.SetMode()` API;
  maps to existing PSYQo camera math, not a new system.
- **Volume** sliders for Music / SFX / UI — backed by `Audio.SetVolume`.
- **Controls** diagram showing the current button mapping.
- Start → Back returns to scene 0.

### scene_2 — Hub (town square)
**Requires:** bullet 7 (non-flat nav), bullet 8 (UI), bullet 11 (skinned
meshes for NPCs), Phase 2.6 (dialog primitive).

A small town-ish area demonstrating the **TownSquare** scene type and its
budget caps.

- 3–4 modular building shells using one shared texture atlas (proves the
  "area texture set" pattern from the optimization reference).
- 2 NPCs standing at static positions (later walking patrols, once Phase 2.5
  AI state machines land):
  - **Shopkeeper** → opens an inventory/shop canvas.
  - **Quest giver** → talks via the dialog tree, sets a quest flag.
- 1 signpost — a `PS1Interactable` that shows a dialog canvas with lore text.
- Door into the dungeon → `Scene.Load(3)`.
- Day/night global tint cycling slowly (Phase 2.5 `World.SetTimeOfDay`).

### scene_3 — Dungeon (combat sample)
**Requires:** bullet 7 (nav), bullet 8 (UI HUD), bullet 11 (skinned enemies),
Phase 2.5 (state-machine AI), Phase 2.6 (attributes, effects, abilities).

Corridor + small room exercising real-time combat.

- Player HP/MP bars, tick counter for stamina-like regen — UI canvas with
  `Progress` elements (runtime already supports).
- 2–3 simple enemies using the `EnemyAI.Melee` stock behaviour from Phase 2.6.
  One shared skeleton, different palette swaps — proves the reference's
  "reuse skeletons aggressively" guideline.
- Player can:
  - Melee attack (Cross) → `Ability.Activate("basic_attack")` → damages
    nearest tagged enemy within a 1.5m cone.
  - Guard (Square) → applies a short defensive tag that reduces incoming
    damage.
- Enemy death → body despawns, optional loot drop (inventory pickup).
- Exit door → scene 4 or back to hub.

### scene_4 — End screen / credits
**Requires:** bullet 8 (UI), bullet 10 (cutscenes — optional).

Plain UI canvas with scrolling credits, ambient loop, D-pad to exit back to
start menu.

## RPG primitives the demo exercises

Once Phase 2.6 ships, the demo uses these as the "canonical examples" page for
authors:

- **Attributes** — player has `Health / MaxHealth / Mana / MaxMana / Stamina`.
- **Effects** — poison DoT applied by one enemy, heal-over-time from the
  shopkeeper's "rest" option.
- **Abilities** — 2 player abilities (basic attack, guard), 1 enemy ability
  (charge).
- **Tags** — `State.Stunned` applied by a heavy hit, `Faction.Hostile` on
  enemies.
- **Inventory** — 3 items: potion (consumable heal), dungeon key (quest),
  torch (equippable, lights a dark area).
- **Dialog** — one branching tree with at least one choice that sets a
  `QuestFlag`.
- **Save/load** — on menu → Save, state written to memory card; Load Save
  brings the player back to the last hub position with preserved inventory
  + quest flags.

Deliberately **not** included in the demo (scope control):
- Party management
- Magic shops / crafting
- Real-time weather
- Multi-disc content

## Camera modes

The Options menu promises three camera modes. Implementation notes:

- **Third-person (default)** — `Camera.FollowPsxPlayer` already exists on the
  Lua API. Offset behind and above the player, look-at player + a small
  forward vector.
- **First-person** — camera snaps to player head position, rotation locked to
  player facing. Simplest mode in code; main risk is authoring (disables seeing
  the character model; needs "hide player mesh" hook).
- **Orbit (free camera)** — right stick rotates around a player-anchored
  pivot. Useful for screenshots and as a debug mode.

All three share the same underlying `Camera.*` primitives; the mode switch is
which Lua routine runs per-frame.

## Budget headroom per scene

Following the PS1 optimization reference's "scene budgeting" guidance, target
caps (enforced by the Phase 3 overlay, authored today in `PS1Scene.*Budget`
fields — already landed as REF-GAP-4):

| Scene           | Type              | TargetTris | MaxActors | MaxEffects | MaxTexPages |
|-----------------|-------------------|-----------:|----------:|-----------:|------------:|
| start menu      | Menu              |        200 |         0 |          4 |           2 |
| options         | Menu              |        200 |         0 |          4 |           2 |
| hub / town      | TownSquare        |       2500 |         6 |         16 |           6 |
| dungeon         | DungeonCorridor   |       1800 |         5 |         24 |           5 |
| end / credits   | CutsceneCloseup   |        300 |         1 |          2 |           2 |

Tune as the demo takes shape. Exceeding any cap should get a warning in the
Phase 3 overlay, not a hard block — authors sometimes knowingly trade one
budget against another.

## Asset reuse

The demo ships one compact asset kit to reinforce the reference's reuse-first
stance:

- **One environment atlas** across town + dungeon (stone, wood, ground, trim).
- **One character atlas** across player + NPCs + enemies (palette swaps for
  variation).
- **One UI atlas** across all menus + HUD.
- **Two fonts** total: system font for HUD; one larger accent font for menu
  headers (exercises bullet 8's custom font path once that lands).

That's three atlases + two fonts for the whole demo — a concrete example of
"area texture sets" fitting inside 1 MB VRAM.

## Build-up order

Don't wait for every bullet to ship. Expand the demo in lockstep with the
exporter. Suggested order once bullet 8 MVP lands:

1. **Bullet 8 lands** → add start menu (`scene_0`). Options / credits are
   cosmetic variants using the same UI system.
2. **Bullet 7 lands** → rebuild the current demo scene as `scene_2` hub with
   non-flat nav regions.
3. **Phase 2.5 AI lands** → add wandering NPCs to the hub.
4. **Bullet 11 lands** → swap NPC cubes for skinned-mesh characters.
5. **Phase 2.6 lands** → add dungeon (`scene_3`) with combat, attributes,
   abilities.
6. **Phase 2.5 save lands** → wire Options → Save Slot to memory card.

Each step is a shippable demo state — the blueprint is not a "all-or-nothing"
gate.

## Scope guardrails

- Demo content is authored by us; the goal is **show, not impress**. Assets
  stay modular, small, and reusable rather than showcase-quality.
- If a demo feature requires a new exporter hook or runtime patch, land the
  hook as a normal bullet, not as a one-off demo shim.
- The demo must **fit in the budget tables above** — if it doesn't, either
  trim the content or raise the budget with a reference citation, never
  silently exceed.
