# Roadmap

Authoring PlayStation 1 games in Godot, using psxsplash as the PS1-side runtime.

## Strategy in one paragraph

SplashEdit (Unity) and psxsplash (C++ on PS1) talk through a versioned binary
"splashpack" file. Rather than rewriting the runtime or forking Godot, we replace
only the authoring half: a Godot editor plugin (+ GDExtension for hot paths)
that walks a Godot scene tree, applies PS1 constraints, and writes a splashpack
that psxsplash loads unchanged. PCSX-Redux is the iteration target; real hardware
comes later.

## Design principles

The plugin is the delivery vehicle. The UX bar is **better than SplashEdit**,
not parity. Concretely:

- **Lua is a first-class Godot script language**, not a separate asset. Attach
  `.lua` directly to a node via `ScriptLanguageExtension` (GDExtension); let
  authors hit F1 on a Lua function and land in the API docs.
- **F5 to play** — pressing Play in Godot should build a splashpack, launch
  PCSX-Redux with PCdrv pointed at it, and attach a debugger. No "Control Panel"
  roundtrip.
- **Hot-swap, not full rebuild.** With PCdrv + a running emulator, re-exporting
  one asset should reload just that asset where possible.
- **Autocomplete everywhere.** Generate EmmyLua stubs from `psxsplash-main/src/luaapi.hh`
  at plugin install so Rider/VSCode know every API call.
- **Constraints visible at author time.** VRAM viewer, SPU budget bar, BVH
  cost indicator live in the viewport, not hidden in a separate window.
- **Project template.** `PS1 Game` appears in Godot's New Project dialog;
  one click, you have a booting scene.
- **Distribution (Phase 4):** bundle vanilla Godot .NET + plugin + template
  into a `PS1Godot.zip` drop. Same end-user feel as a fork, zero merge pain.

When a feature sounds like "SplashEdit had it, let's port it", also ask "what
would the Godot-native version look like?"

---

## Phase 0 — Environment (est. 1–2 days)

Prove the vendored pieces work before writing any new code.

- [ ] Install MIPS toolchain (`mips install 14.2.0` via `pcsx-redux-main/mips.ps1`).
- [ ] Build psxsplash with PCdrv backend: `make all -j PCDRV_SUPPORT=1`.
- [ ] Run a SplashEdit sample export in PCSX-Redux end-to-end to confirm the
      reference pipeline works on this machine.
- [ ] Install Godot 4.x **Mono/.NET** build. Create a throwaway C# script,
      confirm build + hot reload.
- [ ] Confirm the splashpack version (currently **v20**, three-file split) still
      matches between SplashEdit and psxsplash `HEAD`. Update `CLAUDE.md` and
      `docs/splashpack-format.md` if it has moved.

**Done when:** a SplashEdit-authored scene boots in PCSX-Redux on this machine,
and a "hello world" Godot C# project builds.

---

## Phase 0.5 — Onboarding automation (est. 3–5 days)

*Can run in parallel with Phase 1 / 2.*

Getting from "fresh Windows machine" to "Godot editor open with PS1 plugin"
currently takes ~60 minutes, eight manual steps, and at least one "oh that
was silent" moment. Target: **under 10 minutes, mostly downloads, zero
terminal commands.**

Concrete pain observed during our own first setup (evidence, not speculation):

- Dev SDK isn't on nuget.org → requires `GODOT_NUPKGS` env var wire-up.
- MIPS installer runs silently → no confirmation install succeeded.
- MIPS version number in docs went stale (`14.2.0` → `15.2.0`).
- Three env vars across three install paths with no diagnostic to verify.
- No PCSX-Redux installer script.
- `make` / MSYS2 assumed present, not checked.
- PATH refresh requires reopening terminals, easy to forget.
- psxsplash submodules missing on zip-download → silent build break.

### Strategy: the plugin IS the installer

The user only ever interacts with **two** things: Godot itself, and the
plugin folder dropped into `addons/`. Once the plugin is enabled, it
detects what's missing and offers in-editor "Install …" buttons. No
`.cmd` files, no terminal, no env-var docs. The terminal scripts in
`scripts/` stay as power-user backups but aren't the primary path.

### Deliverables

- [ ] **In-editor "PS1 Setup" panel** (`addons/ps1godot/setup/SetupPanel.cs`):
  - Detects: Godot version, `dotnet`, MIPS toolchain, `make`, PCSX-Redux,
    psxsplash submodules, env vars.
  - For each missing piece: shows a row with the dependency name, status
    (✓/✗), and an "Install" button that downloads + extracts via
    `HTTPRequest` + `ZIPReader` to a sensible location (`%APPDATA%\PS1Godot\`).
  - On success, sets the relevant env var (`GODOT_EXE`, `PCSX_REDUX_EXE`)
    via `[Environment]::SetEnvironmentVariable` shelled out for persistence.
  - Bottom of panel: "Re-check all" + plain-language summary
    ("3 of 5 dependencies installed; click Install on the rows above").
  - Auto-opens once when the plugin is first enabled and detects missing
    deps. After everything is green, never auto-opens again.
- [ ] **`scripts\install-deps.cmd`** — power-user backup that wraps the same
      logic the panel uses (so CI / scripted setups have a non-GUI path).
- [ ] **`scripts\doctor.cmd`** — pass/fail diagnostic mirroring the panel's
      detection logic, for users who prefer a terminal report.
- [ ] **Default the project to stable Godot** (4.4.x or latest stable at time
      of Phase 0.5). Dev builds stay opt-in behind a `godot-ps1/NuGet.Config`
      switch — documented in SETUP.md but not the default.
- [ ] **Replace "reopen terminal" steps** with `refreshenv` (via Chocolatey
      helper, or a bundled mini-utility) so the installer and doctor work in
      one continuous session.
- [ ] **Winget/Scoop fallback** — if either is present, prefer
      `winget install GodotEngine.GodotEngine` etc. over custom download.
- [ ] **psxsplash submodule auto-fix** — if `third_party/nugget/` is empty,
      the panel offers a "Fetch psxsplash dependencies" button that runs
      `git clone --recursive` for the missing submodule (or shells `git
      submodule update --init --recursive` if the user has psxsplash as a
      proper checkout).
- [ ] **Asset Library publication** — once the panel is solid, publish the
      plugin to Godot's Asset Library so installation becomes "search 'PS1Godot'
      in Godot's AssetLib tab → click Install."
- [ ] **Pinned-version + update notification system.** Plugin ships
      `addons/ps1godot/dependencies.json` listing the **tested** versions of
      every dep (psxsplash commit/tag, PCSX-Redux release tag, MIPS toolchain
      version, splashpack format version). Setup panel detects installed
      versions, shows "installed vs pinned" per row with an "Update to X"
      button. **Never auto-updates** — surprise breaks are worse than being
      slightly behind. Lock file at `%APPDATA%\PS1Godot\installed.json`
      records what's actually present.
- [ ] **Splashpack format compatibility check.** Writer stamps the format
      version it produces (v20 today). Setup panel detects the splashpack
      version the installed psxsplash *expects* (parse the `version >= N`
      assert in `splashpack.cpp` at install time, or shell `psxsplash.elf
      --print-format-version` if/when we add that flag) and warns when they
      diverge: "Your psxsplash expects v21 but the plugin only writes v20.
      Update plugin first." Prevents silent export corruption when runtime
      and writer drift apart.
- [ ] **SETUP.md becomes a backup**, not the primary path. Primary path is
      now: install Godot → drop addon into `addons/` → enable plugin →
      click "Install" on each missing dep in the auto-opened panel.

### Done when

A collaborator on a fresh Windows VM with only Godot installed can drop the
plugin into `addons/`, enable it, click through the setup panel, and have
the demo scene exporting + booting in PCSX-Redux — without ever opening a
terminal or reading docs. Total wall time under 10 minutes (most of which
is downloads).

---

## Phase 1 — In-editor PS1 preview (est. 1–2 weeks)

Give the author a PS1-looking viewport *inside Godot* with no export yet.
Value: you can design and iterate on look & feel without waiting on the emulator.

- [x] Set up `godot-ps1/` as a Godot 4.x C# project.
- [x] Addon skeleton: `addons/ps1godot/` with `plugin.cfg` and an `EditorPlugin`.
- [x] Custom nodes: `PS1Scene`, `PS1MeshInstance`, `PS1Camera` via
      `[GlobalClass][Tool]` C# classes.
- [x] PS1 spatial shader (vertex snap, 2× color modulate, fog, nearest filter,
      unshaded + baked vertex color for Gouraud-style lighting).
- [x] Demo scene showing the look. **Verified 2026-04-18:** vertex jitter
      visible while orbiting cubes in the editor viewport.
- [x] Low-resolution runtime viewport — 320×240 render integer-scaled to
      1280×960 at runtime via `project.godot` stretch settings. Editor-time
      low-res preview via `CompositorEffect` deferred to Phase 1 stretch
      (complexity on 4.7-dev.5 isn't worth the risk right now).
- [x] Vertex-subdivision aid — `PS1MeshSubdivider` + tool menu item
      "PS1Godot: Subdivide Selected Mesh (×4 tris)". Splits each triangle of
      a selected `MeshInstance3D` into 4, amplifying affine warp.
- [x] Texture compliance analyzer — `PS1TextureAnalyzer` + tool menu item
      "PS1Godot: Analyze Texture Compliance". Walks `res://` images, reports
      per-texture CLUT verdict (4bpp / 8bpp / 16bpp direct / too-big) and
      estimated VRAM cost, with total-vs-1 MB budget. Full `EditorImportPlugin`
      integration deferred to Phase 3 (alongside VRAM viewer dock).
- [x] Stretch: editor-viewport low-res preview via `CompositorEffect` —
      `PS1PixelizeEffect` + `ps1_pixelize.glsl` compute shader. Downsample
      viewport color to 320×240 scratch, nearest-upsample back. Attached
      per-camera via `Compositor`. Menu item: "PS1Godot: Toggle PS1 Preview
      on Selected Camera". Marked experimental on 4.7-dev.5 — graceful
      fallback if init fails.
- [x] Stretch: PS1Lua as a first-class Godot script language. Implemented as
      a **C++ GDExtension** (`addons/ps1godot/scripting/`) after the C# path
      hit a Godot 4.7-dev.5 binding bug (PascalCase/snake_case mismatch in
      the dispatcher). godot-cpp's `GDCLASS` macro routes virtuals via the
      snake_case StringNames the engine emits, sidestepping the bug
      entirely. `PS1LuaScriptLanguage` + `PS1LuaScript` register at
      `MODULE_INITIALIZATION_LEVEL_SCENE` (before `ScriptCreateDialog`'s
      one-shot enumeration), and `PS1LuaResourceFormatLoader` /
      `PS1LuaResourceFormatSaver` handle `.lua` persistence. PS1Lua appears
      in the Create Script dropdown alongside GDScript and C#, and creates
      template `.lua` files on disk. Build via `scons` from the addon dir.

**Done when:** author opens Godot, drops meshes into a `PS1Scene`, and the
viewport looks recognizably PS1 (jitter, low-res, 15-bit color, affine warp).

**Out of scope:** exporting anything. That's Phase 2.

---

## Phase 2 — Splashpack exporter MVP (est. 3–5 weeks)

Port SplashEdit's writer to Godot C#. This is the load-bearing phase.

Work in roughly the order the binary format is laid out, not in order of "what
feels fun". Each sub-milestone should produce a splashpack that psxsplash loads
without crashing, even if features are stubbed.

1. **Writer skeleton + 3-file split.** Port `PSXSceneWriter.Write()` structure
   and offset bookkeeping. Emit an empty but valid v20 splashpack plus its empty
   `.vram` and `.spu` sidecar files. Confirm psxsplash boots into an empty
   scene. *(port from `PSXSceneWriter.cs`)*
2. **Static meshes + VRAM textures.** Port `PSXObjectExporter`, `TexturePacker`,
   `ImageProcessing`, `PSXMesh`. Get one textured cube rendering in PCSX-Redux.
3. **Collision + BVH.** Port `PSXCollisionExporter`, `BVH`. Player can walk on
   a floor.
4. **Player + camera config.** Map Godot camera/player settings to splashpack
   player fields (position, rotation, height, speeds, gravity).
5. **Lua scripting path.** Wire `luac_psx` into the export pipeline; Godot
   scripts-as-lua or separate `.lua` assets — decide in Phase 2.5.
6. **Audio.** Port ADPCM conversion, `PSXAudioClip`, `PSXAudioEvent`.
7. **Nav regions.** Port `PSXNavRegionBuilder` (it wraps DotRecast — check if a
   .NET port is usable from Godot C# directly).
8. **UI canvases + fonts.** Port `PSXCanvas*`, `PSXFontAsset`, `PSXUI*`.
9. **Trigger boxes, interactables.** Port `PSXTriggerBox`, `PSXInteractable`.
10. **Cutscenes + animations.** Port `PSXCutscene*`, `PSXAnimation*`.
11. **Skinned meshes.** Port `PSXSkinnedMeshExporter`, `PSXSkinnedObjectExporter`.
12. **Rooms / portals (interior scenes).** Port `PSXRoom`, `PSXPortalLink`.

**Parity test:** take three reference scenes that ship with SplashEdit, rebuild
them in Godot, and byte-diff the resulting splashpacks against the Unity output.
Non-zero diffs must be explainable (e.g., floating-point ordering), not bugs.

**Done when:** a non-trivial game scene authored only in Godot runs in PCSX-Redux
with rendering, collision, audio, UI, nav, and Lua all working.

---

## Phase 2.5 — Runtime capability expansion (est. open-ended)

*Phase 2 ports the asset pipeline; Phase 2.5 is about what games authored with
that pipeline can actually express.*

psxsplash + Lua today is shaped for "author a static world, attach behavior"
(Spyro/Crash shape). Ambitious projects — RPGs, survival games, procedural
worlds, complex enemy AI — need more from the Lua surface. PS1 silicon itself
is not the ceiling: PSYQo exposes the GPU/GTE/SPU directly, and PSn00bSDK
demos (Minecraft-on-hardware at 135 KB) prove the hardware is more than
capable. Our constraint is the runtime layer's shape.

The layer cake:

```
Hardware  →  SDK (PSYQo / PSn00bSDK)  →  Runtime (psxsplash)  →  Scripts (Lua)
fully capable     thin + general          opinionated          what authors see
```

The 135 KB hand-rolled demo went hardware ↔ SDK, two layers. We go through
four — and the runtime layer is where "the world is a fixed set of authored
GameObjects" gets baked in.

Organized by theme, not priority. Most items are "bind more of PSYQo /
psxsplash-internal into `luaapi.cpp`" — small, additive, ship-in-any-order.
Items needing runtime architecture changes (dynamic mesh queue, object pool,
chunk streaming, memory-card syscalls) are flagged **[runtime]** and should
be proposed upstream to psxsplash rather than hacked into the vendored tree
(tracker: `docs/psxsplash-improvements.md`).

### Dynamic content creation

Every GameObject is export-time today. Unlocking runtime spawning opens up
bullets, pickups, particles, enemy waves, and voxel-style worlds.

- [ ] `GameObject.Spawn(templateIdx, pos, rot)` + `GameObject.Destroy(self)` —
      template pool in the splashpack, free-list in the runtime. **[runtime]**
- [ ] `Mesh.Submit(verts, tris, tpage, aabb)` — Lua-built meshes submitted
      per-frame. Enables voxel chunks, procedural terrain, dynamic decals.
      **[runtime]**
- [ ] `VoxelMesh.Build(grid, blockSize, atlasTpage)` — voxel 3D-array → PSX
      triangle list with **hidden-face culling** (skip faces touching a solid
      neighbour — cuts tri count 5–10×). Inner loop in C++; Lua just supplies
      the grid.
- [ ] `Scene.LoadChunk(index, origin)` / `Scene.UnloadChunk(id)` — partial
      scene overlay for streaming worlds. Current `requestSceneLoad` does
      full-scene swaps; needs a parallel sub-scene path. **[runtime]**

### Performance / culling / scheduling

Dynamic content costs frames. The renderer already does BVH frustum culling
for static meshes — dynamic content needs the same budget discipline, plus
ways to spread expensive Lua work across frames without stuttering.

- [ ] **Per-chunk frustum culling** — dynamic meshes submitted via
      `Mesh.Submit` carry an AABB; renderer frustum-tests before queuing
      primitives. Already the static-BVH story, extended. **[runtime]**
- [ ] **Time-sliced tasks** — `Task.RunOverFrames(fn, budgetUsPerFrame)` —
      cooperative scheduler sips CPU per frame instead of blocking. For
      chunk rebuilds, pathfinding, nav updates. **[runtime]**
- [ ] **Coroutines as first-class control flow** — `coroutine.yield(frames)`
      inside `onUpdate`; scheduler resumes on the right tick. Natural syntax
      for "patrol 2s, turn, patrol 2s" without state-machine boilerplate.
- [ ] `Debug.Profile("chunk-rebuild", fn)` — per-frame timing ring buffer
      surfaced in the dev overlay. Essential once Lua starts doing real work.

### Procedural / math

World-gen basics Lua stdlib doesn't provide.

- [ ] `Math.PerlinNoise2/3`, `Math.SimplexNoise2/3` — seeded, deterministic,
      fp12 internals (float round-trip is wasteful on MIPS).
- [ ] `Math.Random(seed)` seeded RNG distinct from the shared global stream.
- [ ] `Math.Hash(x, y, z)` — fast stable cell hash.
- [ ] `Math.Lerp/Clamp/SmoothStep` / `Math.Fixed.*` convenience.

### Physics & spatial queries

The runtime already has a collider grid and nav regions — bind them to Lua.

- [ ] `Physics.Raycast(origin, dir, maxDist)` → hit (normal, distance, object).
      Unlocks targeting, line-of-sight, "look at block, break block."
- [ ] `Physics.OverlapBox/Sphere(bounds)` → list of intersecting GameObjects.
- [ ] `Physics.SweepSphere(origin, dir, radius)` for projectiles.
- [ ] `Nav.Pathfind(from, to, maxSteps)` — portal-aware path across nav
      regions. Depends on Phase 2 bullet 7 landing the nav data.

### AI building blocks

The reference PSn00bSDK demo used explicit state machines for enemy AI.
Instead of every script re-implementing `if state == "patrol" then …`,
ship the primitive.

- [ ] `StateMachine.new({ patrol={enter,update,exit}, chase=…, attack=… })`
      — per-object FSM with transition callbacks. State id + timer on the
      GameObject struct keeps hot paths out of Lua.
- [ ] `AI.DistanceToPlayer(self)` — convenience (every enemy script wants it).
- [ ] `AI.LineOfSightTo(self, target)` — raycast wrapper.
- [ ] `AI.Steering.Seek/Flee/Wander(self, target, params)` — classical
      steering primitives; cheap to implement, huge ergonomic win.

### Audio from Lua

Assets pack via Phase 2 bullet 6; Lua can't trigger them yet.

- [ ] `Audio.Play(clipIdx, vol, pitch)` / `Audio.Stop(handle)`.
- [ ] `Audio.Play3D(clipIdx, worldPos)` — positional via SPU per-voice volume.
- [ ] `Music.PlayXA(track)` / `Music.Stop()` / `Music.SetVolume(v)`.

### Save / load

PS1 BIOS has memory-card syscalls; nothing in psxsplash surfaces them today.

- [ ] `Save.WriteSlot(slot, table)` / `Save.ReadSlot(slot)` — serialize a
      Lua table (whitelisted types). **[runtime]**
- [ ] `Save.ListSlots()` for a load menu.
- [ ] Memory-card ops are slow; surface as async with a "saving…" UI state.

### Full transform

`Entity.SetRotationY` is the entire rotation API today.

- [ ] `Entity.GetRotation/SetRotation` (full Euler or quaternion — pick one).
- [ ] `Entity.GetTransform/SetTransform` → matrix.
- [ ] `Entity.GetScale/SetScale` iff the renderer supports non-uniform scale
      (may be no-op on vertex-baked meshes; check before promising).

### UI / HUD from Lua

Canvas assets export; runtime mutation doesn't.

- [ ] `UI.SetText(canvas, slot, str)` — dialog, inventory counts, health.
- [ ] `UI.SetColor(canvas, slot, r,g,b)` — flash-on-damage.
- [ ] `UI.Show/Hide(canvas)`, `UI.SetImage(canvas, slot, atlasIdx)` for
      animated icons.

### Scene queries, tags, messaging

- [ ] `GameObject.SetTag/GetTag(self, tag)` (uint16 on the struct).
- [ ] `Scene.FindByTag(tag)` → list; `Scene.FindNearest(pos, tag)`.
- [ ] `Entity.Send(target, "event", args...)` — custom event dispatch,
      observer pattern without polling.
- [ ] `Scene.GetSharedState() → table` — scene-scoped state that all scripts
      see (currently each script sandboxes against `_G` gymnastics).

### Input

- [ ] `Input.IsPressed(pad, btn)` — multi-pad; current API implicitly pad 0.
- [ ] `Input.JustPressed(btn)` single-frame edge trigger.
- [ ] `Input.SetRumble(pad, intensity, frames)` — analog controllers support it.

### Cutscenes / flow control

Runtime has cutscene playback; Lua can't trigger it.

- [ ] `Cutscene.Play(name)` / `Stop()` / `IsPlaying()`.
- [ ] `Scene.SetPaused(bool)` — freeze game clock for menus / inventory.

### World simulation (day/night, weather)

Survival and open-world games need world state that evolves over time
independently of scripted events. PS1 can't afford real-time dynamic lighting,
but it can afford cheap global modulation.

- [ ] `World.GetTimeOfDay()` / `World.SetTimeOfDay(hour)` — normalized 0–24h
      clock running on the scene timer.
- [ ] `World.SetDayLength(seconds)` — wall-clock seconds per in-game day.
- [ ] **Global color tint** that modulates every vertex color before GTE
      transform — sunrise orange, night blue, sunset purple. One multiply
      in the hot path, essentially free. **[runtime]**
- [ ] **Fog color / density animation** — already have a fog header; expose
      `World.SetFog(r,g,b,density)` so Lua can fade it over the day clock.
- [ ] **Skybox / sky gradient swap** — either a palette swap (cheapest) or
      a full-screen gradient quad. **[runtime]**
- [ ] `World.SetWeather(kind)` — stub hook for rain/snow particle systems
      once the particle pipeline is in place.
- [ ] NPC schedule hooks — `onTimeOfDay(hour)` event so scripts can react to
      dusk/dawn without polling the clock every `onUpdate`.

### Dev / iteration

- [ ] **Lua hot-swap over PCdrv** — re-export one `.lua`, runtime picks up
      without reboot. Complements Phase 3 "F5 to play."
- [ ] `Debug.Watch("name", value)` — live watch panel in the overlay.
- [ ] `Debug.Assert(cond, msg)` with stack dump (silent Lua failures today).

### Bytecode pipeline (deferred from Phase 2 bullet 5)

- [ ] Compile `.lua` → PSX bytecode at export via `luac_psx` headless in
      PCSX-Redux. Smaller splashpacks, enables `NOPARSER=1` runtime build
      (~25 KB RAM back — useful once scripts grow).

**Done when:** a procedural/dynamic game (voxel sandbox, roguelite, survival
prototype) authored only in Godot + Lua runs in PCSX-Redux at playable
framerate, without requiring direct psxsplash forks for gameplay reasons.

---

## Phase 2.6 — RPG / action-RPG toolkit (est. open-ended)

*Genre-specific convenience layer built on Phase 2.5 primitives. Optional;
skip entirely if you're not making an RPG. Inspired by UE5's Gameplay
Ability System (GAS), dumbed down to fit 2 MB RAM and fixed-size budgets.*

UE5's GAS has four load-bearing concepts: **Attributes**, **Effects**,
**Abilities**, and **Tags**. Those map cleanly onto PS1 if we drop the
parts that exist only for network prediction and runtime-generated content.
What's left is enough to build action-RPGs shaped like *Fable*, *Dark Souls*,
*Skyrim*, or *Diablo* — stats, buffs/debuffs, skills with cooldowns,
equipment, status effects.

### Design tenets

- **Static authoring, dynamic state.** Abilities, effects, items, and their
  parameters are authored in Godot and baked into the splashpack. Only
  *state* (current HP, active effects, equipped items, ability cooldowns)
  lives at runtime. No runtime-generated ability objects.
- **Fixed budgets.** `MAX_ATTRIBUTES`, `MAX_EFFECTS_PER_ENTITY`,
  `MAX_ABILITIES_PER_ENTITY`, `MAX_INVENTORY_SLOTS` — compile-time
  constants with sane defaults (32 / 8 / 16 / 64). Pre-allocated arrays,
  zero heap churn in combat.
- **Tags are bitfields, not hierarchies.** 64 gameplay tags per entity as
  one `uint64`. "Hierarchy" (`State.Stunned` vs `State.Stunned.Heavy`)
  collapses to "pick your 64 tags carefully." Bump to 2× `uint64` if a
  project really needs 128.
- **Integer / fp12 math only.** HP, damage, cooldown frames — integers or
  fp12. No float round-trips through the GTE path.

### Attributes

Per-entity numeric stats. Authored as a fixed struct in the editor so the
compiler knows field offsets; exposed to Lua as `self.stats.health` etc.

- [ ] `Attr` module: `Get(self, name)`, `Set(self, name, val)`,
      `GetMax(self, name)` (current/max pairs for HP/MP/stamina).
- [ ] `Attr.ApplyInstant(self, name, delta)` — the primitive damage/heal op.
      Clamped to [0, max]; fires `onAttributeChanged`.
- [ ] `onAttributeChanged(self, name, oldVal, newVal)` event — drives UI
      updates, death triggers, low-HP warnings.
- [ ] Editor resource type `AttributeSet.tres` with named fields + default
      values. One per character class or enemy type.

### Effects (buffs / debuffs / DoTs)

Timed attribute modifiers — workhorse of every RPG combat system.

- [ ] `Effect` module: `Apply(target, effectId, stacks, durationFrames)` →
      handle; `Remove(handle)`; `Has(target, effectId)`; `Stacks(target,
      effectId)`.
- [ ] `Effect.tres` resources carry: attribute modifiers
      (`{attr, op: add/mul/override, magnitude}`), periodic tick callback,
      stacking rule (none/stack/refresh/highest-wins), granted tags while
      active, removal conditions.
- [ ] `onEffectApplied(self, effectId)` / `onEffectRemoved` — cosmetic cues.
- [ ] Periodic ticks in fp12 frames ("deal 5 damage every 30 frames") —
      scheduler runs in the main update loop, zero per-effect Lua
      allocation.

### Abilities

Activatable actions. Simplified GAS: cost, cooldown, cast time, targeting
kind, effect list.

- [ ] `Ability` module: `Activate(caster, abilityId, targetData)` →
      `(success, reason)`; `GetCooldown(caster, abilityId)`;
      `IsReady(caster, abilityId)`.
- [ ] `Ability.tres` resources: cost (which attribute, how much), cooldown
      frames, cast-time frames, required tags, blocked-by tags, target
      kind (self/single/AoE-sphere/cone), effect list applied on hit,
      animation clip, audio cue.
- [ ] `onAbilityActivated/onAbilityEnded` — combo chains, reactive
      abilities.
- [ ] Targeting helpers: `Target.Single(self, maxRange, tag)` — closest
      tagged target in range; `Target.Sphere(pos, radius, tag)` → list.

### Tags

Fast gameplay filters. Bitfield per entity; authored constants in a central
registry so bit indices stay stable.

- [ ] `Tag.Has(self, TAG)` / `Tag.HasAny/HasAll(self, mask)`.
- [ ] `Tag.Add(self, TAG)` / `Tag.Remove(self, TAG)`.
- [ ] `Tag.Registry` — authored enum (`TAG_STUNNED = 1`, `TAG_INVULNERABLE
      = 2`, …) shared between Lua and C++. Editor UI for picking tags
      from a list rather than typing bit indices.

### Inventory & equipment

- [ ] `Inventory` module: `Add(self, itemId, count)`, `Remove`, `Has`,
      `Count`, `Equip(self, itemId, slot)`, `Unequip(self, slot)`.
- [ ] `Item.tres` resources: type (consumable/equipment/quest), equipped
      slot (weapon/armor/ring/…), passive effects granted while equipped
      (reuses the Effect system), on-use effects (consumables).
- [ ] Slot count is fixed; overflow is authored per-inventory
      (drop/reject/replace-oldest).

### Leveling & progression

- [ ] `Progression` module: `AddXP(self, amount)` with automatic
      `onLevelUp(self, newLevel)` when authored thresholds are crossed.
- [ ] `LevelCurve.tres`: XP thresholds, attribute growth per level (flat
      or curve-sampled).

### Dialog / quest state

- [ ] `Dialog.Start(nodeId)` / `Dialog.Choose(choiceIdx)` — tree-walker
      over authored dialog resources.
- [ ] `DialogNode.tres`: text, choices, conditions
      (`QuestFlag.Has("met_NPC")`), effects (`QuestFlag.Set(...)`).
- [ ] `QuestFlag.Set/Get/Has` — uint32 bitfield per category, persisted
      to save.
- [ ] `Faction.GetReputation(self, factionId)` / `AddReputation` for
      disposition-tracking NPCs.

### AI integration (opinionated presets)

Reuses Phase 2.5 state machines + steering + raycast, but ships
RPG-shaped presets so authors don't start from a blank state diagram.

- [ ] Stock behaviours: `Patrol`, `Aggro`, `Flee`, `Melee`, `Ranged`,
      `Boss` — each wires a state machine with sensible defaults.
- [ ] `Aggro.OnSight/OnDamage/OnAlly` triggers.
- [ ] `Pack` behaviour — coordinate across tagged entities (wolf packs,
      guards calling reinforcements).

### Save integration

Builds on Phase 2.5's `Save.*` memory-card API.

- [ ] `Save.SerializeEntity(self)` / `Save.RestoreEntity(self, data)` —
      persists attributes, active effects (with remaining duration),
      inventory, equipped slots, ability cooldowns, known abilities.
      Whitelist-based — only fields flagged "persisted" in the editor
      make the round trip.
- [ ] `Save.SerializeWorld()` — scene-level state (quest flags, NPC
      dispositions, time of day, weather).

**Done when:** a vertical-slice RPG (1 town, 5 quest NPCs, 3 dungeon enemy
types, 6 abilities, 20 items) authored only in Godot + Lua runs in
PCSX-Redux with combat, inventory, leveling, dialog, and save/load working.

---

## Phase 3 — Author experience wins (est. 2–4 weeks)

SplashEdit has useful UX that makes PS1 constraints tractable. Port what
matters, and do better where it's cheap.

- [ ] **F5 to play.** Hook Godot's Play button: build splashpack → launch
      PCSX-Redux with PCdrv → attach C# debugger → tail `printf` output in
      the Godot Output dock.
- [ ] **VRAM viewer** as a dockable panel (not a separate window like SplashEdit).
- [ ] **SPU / memory / BVH budget bars** in the viewport overlay.
- [ ] Quantized texture preview in the inspector for any PS1Texture asset.
- [ ] EmmyLua stub generation from `luaapi.hh` on plugin load; dropped into
      `.godot/ps1godot/lua-stubs/` for Rider/VSCode to pick up.
- [ ] **PS1Lua syntax highlighting in Godot's built-in script editor.**
      Subclass `EditorSyntaxHighlighter` in the GDExtension; register via
      `ScriptEditor::register_syntax_highlighter`. Keywords + delimiters
      already declared in `_get_reserved_words` etc. — wire to the
      highlighter and pick a color palette.
- [ ] **PS1Lua autocomplete in Godot's built-in script editor.** Implement
      `ScriptLanguageExtension::_complete_code` with at least: keyword
      completion, local-variable completion (single-pass tokenizer), and
      function names from the EmmyLua stubs above. Full Lua semantic
      completion is out of scope without a real parser — defer Tree-sitter
      integration unless authors complain.
- [ ] **More built-in PS1Lua templates** — extend
      `PS1LuaScriptLanguage::_get_built_in_templates` from one stub to a
      handful (input handler, trigger callback, animated prop, dialog
      driver). Pulls from `psxsplash-main/src/luaapi.hh` patterns.
- [ ] Lua hot-swap: re-exporting a single `.lua` while the emulator is running
      re-uploads only that bytecode via PCdrv.
- [ ] Project template (`PS1 Game`) installable into Godot's project manager.
- [ ] ISO build path via `mkpsxiso` for real-hardware testing.
- [ ] Loading screens (uses existing PSXCanvas path; just a convention).

**Done when:** someone who's never seen the project can open Godot, select the
"PS1 Game" template, press F5, and see a playable scene in the emulator within
60 seconds.

---

## Phase 4 — Distribution + stretch wins (optional)

Only after Phases 0–3 land.

- **`PS1Godot.zip` — the zero-setup endgame.** Single .zip drop containing:
  - Vanilla Godot .NET, renamed (`PS1Godot.exe`).
  - Plugin pre-installed into `addons/`.
  - `PS1 Game` project template pre-registered.
  - PCSX-Redux + OpenBIOS bundled and configured.
  - MIPS toolchain pre-extracted to a sibling dir with PATH wired on
    first run.
  - Launch scripts with paths baked relative to the drop root.
  - Target install time: **double-click extract, done**. No env vars,
    no NuGet.Config, no toolchain download.
  - This is what Phase 0.5 gracefully evolves into once it stabilizes.
- **Visual Lua debugger** wired into PCSX-Redux's Lua debug interface
  (breakpoints, variables, step) surfaced in a Godot dock.
- **GDExtension hot path** for heavy inner loops (texture quantization, BVH
  build, VRAM pack) if iteration time hurts.
- Serial-link upload to real hardware.
- Multi-scene / persistent-data workflow with Godot-native resource references.
- Upstream patches / feedback to psxsplash based on what we learn. Tracker
  at `docs/psxsplash-improvements.md`.
- Contribute the plugin back to the psxsplash org if they want it.

---

## Resolved decisions

- **Plugin + GDExtension, not engine fork.** Fork would mean perpetual merge
  conflicts against a 1M-LOC C++ codebase for minimal capability gain —
  Godot's `EditorPlugin`, `CompositorEffect`, `ScriptLanguageExtension`,
  `EditorImportPlugin`, and `ResourceFormatSaver` cover every hook we need.
  Re-open only if we hit a concrete engine-side wall.
- **Lua authoring UX.** Lua is a first-class Godot script language via
  `ScriptLanguageExtension`; attach `.lua` directly to nodes. No separate
  asset wrapper like SplashEdit's `LuaImporter.cs`.
- **Language for the plugin.** C# (matches SplashEdit porting, native in
  Godot .NET builds). GDExtension/C++ reserved for hot paths.

## Open questions

- **DotRecast for nav.** Is the .NET port stable enough to pull into a Godot
  C# project, or do we shell out to a CLI?
- **Fork vs. upstream for vendored bugs.** If we hit a SplashEdit/psxsplash
  bug, fork into this repo or file upstream and wait? Default to upstream;
  fork only if blocking.
- **Godot 4.x version pinning.** Resolved for distribution at Phase 0.5:
  **stable minor is the default**, dev builds are opt-in via
  `NuGet.Config` swap. Minimum supported is 4.4 (for `CompositorEffect`).
  Currently developing against 4.7-dev.5 for early feature access.

---

## Non-goals

- Running PS1 hardware inside Godot. The emulator is pcsx-redux; don't reinvent.
- A PS1-look asset pack for regular (non-PS1-hardware) Godot games. Orthogonal
  project.
- Supporting consoles other than PS1. PSYQo is PS1-only by design.
