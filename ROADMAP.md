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

**Design constraint document.** `docs/ps1_large_rpg_optimization_reference.md`
is the scoreboard for design decisions against PS1 hardware limits — VRAM,
texture-page churn, ordering-table pressure, disc streaming, chunk structure.
Its "Integration with PS1Godot" section maps the reference onto this repo with
numbered gaps (`REF-GAP-1` … `REF-GAP-10`). Amendments below cite those tags.
When a recommendation here conflicts with the reference, the reference wins
by default — note the override in the PR description.

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
- [x] **Scene categorization + budgets (`REF-GAP-4`).** `PS1Scene.SceneType`
      expanded to the reference's 7 categories (ExplorationOutdoor,
      TownSquare, Interior, DungeonCorridor, Combat, Menu, CutsceneCloseup).
      Authored budget fields `TargetTriangles`, `MaxActors`, `MaxEffects`,
      `MaxTexturePages` on `PS1Scene` feed the future overlay.

**Done when:** author opens Godot, drops meshes into a `PS1Scene`, and the
viewport looks recognizably PS1 (jitter, low-res, 15-bit color, affine warp).

**Out of scope:** exporting anything. That's Phase 2.

---

## Phase 2 — Splashpack exporter MVP (est. 3–5 weeks)

Port SplashEdit's writer to Godot C#. This is the load-bearing phase.

Work in roughly the order the binary format is laid out, not in order of "what
feels fun". Each sub-milestone should produce a splashpack that psxsplash loads
without crashing, even if features are stubbed.

**Status (2026-04-20):** bullets 1–6 ✅, 7 🟡 (authored regions +
auto-portals + ramps; DotRecast auto-gen deferred), 8 ✅ (+ `\n` runtime
wrap, dialog ownership, audio-aware auto-hide via `Audio.GetClipDuration`),
9 ✅, 10 ✅ (camera position + rotation + zoom-on-target tracks; player-rig
handoff convention derived from psyqo's matrix order), 11 ✅, 12 ✅
(rooms + portals + tri-ref assignment + auto cell subdivision + per-room
portal-refs). Format at **v23** (v22 added sequenced music; v23 added UI
3D-model HUD widgets). Multi-scene
packing + `Scene.Load(N)` Lua API also landed on top of the bullet-list
scope.

1. **Writer skeleton + 3-file split.** Port `PSXSceneWriter.Write()` structure
   and offset bookkeeping. Emit an empty but valid splashpack (current format
   is v21) plus its empty `.vram` and `.spu` sidecar files. Confirm psxsplash
   boots into an empty scene. *(port from `PSXSceneWriter.cs`)*
2. **Static meshes + VRAM textures.** Port `PSXObjectExporter`, `TexturePacker`,
   `ImageProcessing`, `PSXMesh`. Get one textured cube rendering in PCSX-Redux.
3. **Collision + BVH.** Port `PSXCollisionExporter`, `BVH`. Player can walk on
   a floor.
4. **Player + camera config.** Map Godot camera/player settings to splashpack
   player fields (position, rotation, height, speeds, gravity).
5. **Lua scripting path.** Wire `luac_psx` into the export pipeline; Godot
   scripts-as-lua or separate `.lua` assets — decide in Phase 2.5.
6. **Audio.** Port ADPCM conversion, `PSXAudioClip`, `PSXAudioEvent`.
   Follow-up in Phase 2.5: per-area SPU budget + `Residency` flag on
   `PS1AudioClip` (`REF-GAP-9`).
7. **Nav regions.** Authored-region path landed 2026-04-20: `PS1NavRegion`
   node (convex polygon, Y-per-vert for ramps), auto plane-fit, auto
   portal-stitching between adjacent region edges. Ramps + stairs inferred
   from slope. Still unchecked: DotRecast auto-generation from floor
   geometry (the "drop your meshes, get a navmesh" SplashEdit flow) —
   tracked below the main list.
8. **UI canvases + fonts.** Port `PSXCanvas*`, `PSXFontAsset`, `PSXUI*`.
   **Amendment (`REF-GAP-8`):** canvases + fonts must carry a `Residency`
   property from day one — `Gameplay | MenuOnly | LoadOnDemand`. Exporter
   keeps menu-only art out of the gameplay resident set; runtime swaps it
   in on `UI.LoadCanvas(name)`. UI VRAM counts against the same budget
   line as environment / character textures (no separate dock).
9. **Trigger boxes, interactables.** Port `PSXTriggerBox`, `PSXInteractable`.
10. **Cutscenes + animations.** Port `PSXCutscene*`, `PSXAnimation*`.
    **Amendment (`REF-GAP-7`):** animation assets carry a residency flag;
    only currently-active-area animations resident.
11. **Skinned meshes.** Port `PSXSkinnedMeshExporter`, `PSXSkinnedObjectExporter`.
    **Amendment (`REF-GAP-7`):** same residency flag applies to skeletons.
12. **Rooms / portals (interior scenes).** MVP landed 2026-04-20:
    `PS1Room` (volume size + offset) and `PS1PortalLink` (RoomA/RoomB
    NodePaths + portal size). Exporter assigns triangles to rooms by
    vertex-majority containment with a 0.5 m boundary expand; resolves
    portal rooms; auto-corrects portal normal to point RoomA → RoomB.
    Catch-all room appended for unassigned triangles. Cell subdivision
    + per-room portal-ref lists landed 2026-04-22 — authored rooms now
    subdivide into an auto 3D grid (~5 m per cell, max 4 per axis) with
    tight per-cell AABBs, and each room carries a portal-ref slice so
    the renderer iterates just its neighbors instead of scanning every
    portal each frame.

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

- [x] `Entity.Spawn(tag, pos [, rotY])` + `Entity.Destroy(obj)` — landed
      under `Entity` namespace (consistent with `Entity.Find`). Pool pattern:
      author sets `Tag` + `StartsInactive = true` on N template instances in
      the editor; Spawn scans for the first inactive match, activates it,
      fires `onEnable`. Also ships `Entity.GetTag`/`SetTag`/`FindByTag`/
      `FindNearest`. No splashpack version bump (repurposed the `_reserved0`
      u16 legacy slot). Per-spawn reset logic should live in `onEnable`,
      not `onCreate`.
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
- [ ] **`PS1Chunk` authoring node (`REF-GAP-5`).** Editor-side container for a
      single streamable chunk: geometry set + resident texture pages + NPC
      set + script set + audio profile + effect budget. One struct, not six.
      Exporter emits a `chunk_N.splashpack` per chunk. Pairs with
      `Scene.LoadChunk` above.

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
- [ ] **FixedPoint ↔ integer conversion helpers.**
      `Math.Floor(fp)` / `Math.Ceil(fp)` / `Math.Round(fp)` → int, plus
      `Math.ToInt(fp)` (truncate) and `Math.ToFixed(int)` (promote).
      Bridges fp12 math into array indexing, tile grids, and counter
      updates without every script re-implementing the shift. Discord
      feature request "FixedPoint to integer conversion functions"
      (psxsplash channel, 2026-04).

### Procedural world primitives

Small layer on top of the procedural/math + dynamic content items above.
Gives authors a "roguelite dungeon" starting point without hand-rolling
graph/BSP algorithms in Lua. Target: one dungeon generated at scene load
from a seed in under 100 ms on hardware.

- [ ] `Layout.RoomsAndCorridors(seed, bounds, roomCount)` → graph of
      rooms + connections. Deterministic from seed.
- [ ] `Layout.BSP(seed, bounds, minLeafSize)` → binary-space-partitioned
      layout for more cave-like shapes.
- [ ] `Kit.PlaceTiles(layout, kit)` — walks a generated layout and emits
      floor/wall/door/pillar meshes from an authored modular kit. Uses
      `Mesh.Submit` per-tile or merges into one `VoxelMesh` call for
      perf. Shares the authored atlas, no new VRAM cost.
- [ ] `Nav.GenerateFromLayout(layout)` — auto-emit one nav region per
      room interior + portals at doors. No DotRecast dependency for
      grid-aligned procedural content.
- [ ] `Collider.GenerateFromLayout(layout)` — one AABB per wall segment.
- [ ] `Pop.Sprinkle(layout, seed, density, templateIds)` — populate with
      enemies / loot / props, respecting per-scene `MaxActors` budget.
- [ ] Save integration: serialize seed + visited-rooms bitmap + chest
      states, **not** the generated grid. Regenerate identically on load.

### Physics & spatial queries

The runtime already has a collider grid and nav regions — bind them to Lua.

- [x] `Physics.Raycast(origin, dir, maxDist)` → `{ object, distance, point }`
      or nil. MVP hits **Solid collider AABBs** (not world-geometry triangles).
      Linear scan up to 64 colliders, slab method in FP12. Normal not returned
      yet — compute from the hit face or the collider's world position until
      ray-vs-triangle lands. Unlocks projectiles, pickups, LoS-to-objects.
- [ ] `Physics.Raycast` against BVH triangles — needed for walls-block-LoS.
- [x] `Physics.OverlapBox({x,y,z}, {x,y,z} [, tag])` → array of object handles.
      AABB-vs-AABB over Solid colliders, optional tag filter. Hard-capped at
      16 results. Used for melee swing hitboxes / area damage.
- [ ] `Physics.OverlapSphere(center, radius)` → list. (OverlapBox covers most
      jam cases; sphere is bonus.)
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
- [ ] **Per-area SPU accounting + `Residency` on `PS1AudioClip` (`REF-GAP-9`).**
      "Ambient loop resident across chunk" vs "event cue streamed in on
      trigger." Counts SPU-RAM per area; warns on overflow.
- [ ] `Music.PlayXA(track)` / `Music.Stop()` / `Music.SetVolume(v)`.
- [ ] **`Audio.SetPitch(handle, semitones)` / runtime pitch control.**
      Real-time pitch shift + slide on a playing voice, not just at
      `Audio.Play` start. Piggy-backs on the 12TET pitch table we
      already ship with the music sequencer — same data, new API
      surface. Discord feature request "Pitch control in Audio"
      (psxsplash channel, 2026-04, 5 upvotes, "considered").
      **[runtime]**

### Sequenced music + music-driven events *(MVP shipped 2026-04-20)*

Upstream psxsplash had no sequenced-music support and no concrete ETA,
so we built our own: a small custom binary format (`PS1M`) parsed at
export from MIDI, played back by a new `MusicSequencer` class on top
of psxsplash's existing `AudioManager`. Format is documented in
`docs/sequenced-music-format.md`; runtime patch tracker entry in
`docs/psxsplash-improvements.md`.

- [x] **`Music.Play(nameOrIndex[, volume])`** — start a sequence. Volume
      is the master 0-127. Returns true on success.
- [x] **`Music.Stop()`**, **`Music.IsPlaying()`**, **`Music.SetVolume(v)`**,
      **`Music.GetBeat()`**, **`Music.Find(name)`**.
- [x] **`PS1MusicSequence` Godot resource** — points at a `.mid` file,
      carries per-MIDI-channel bindings (`PS1MusicChannel` sub-resources)
      that map each channel to a `PS1AudioClip` sample with base note,
      volume, pan, and optional note-range filter (for drum kits
      where one MIDI channel drives multiple sample channels).
- [x] **MIDI parser in C#** — SMF format 0/1, NoteOn/Off + Set Tempo
      meta event. Sort order = NoteOff before NoteOn at same tick (so
      consecutive same-pitch notes don't kill each other through our
      mono-per-channel runtime).
- [x] **PS1M serializer** — flat array of 8-byte events (tick, channel,
      kind, data1, data2). 16-byte header + 8-byte channel entries +
      events. ~12 KB for a 2-minute song.
- [x] **Splashpack v22** — header tail grew 8 bytes for music table
      offset + count. Music section = array of 24-byte
      `MusicTableEntry` + per-sequence PS1M blobs aligned to 4 bytes.
- [x] **Voice reservation** — `AudioManager::reserveVoices(n)` +
      `playOnVoice(...)`. The sequencer claims voices `0..channelCount-1`
      for the song's lifetime. Dialog/SFX use voices `[n, MAX_VOICES)`.
      No more "music note silenced because dialog stole its voice."
- [x] **Dialog auto-ducking** — dialog scripts `Music.SetVolume(18)` on
      show, restore the master on hide. Applied across `test_logger.lua`,
      `checkered_dialog.lua`, `realm_cube_dialog.lua`.
- [x] **Drum-kit support** — per-note routing on `PS1MusicChannel`
      (`MidiNoteMin`/`MidiNoteMax`) lets one MIDI channel drive multiple
      sample channels. Demo wires kick (note 36) → `inst_kick`, snare
      (40) → `inst_snare`, hat (42) → `inst_hat`.
- [ ] `onMusicEvent(cueName, beatNumber)` — Lua callback the runtime
      dispatches when the sequence hits an author-placed event marker.
      Format reserves event kinds 4–255 for this; runtime parser will
      dispatch on a new kind without a header bump. Unlocks:
        - rhythm-game inputs keyed to the beat,
        - boss phase transitions triggered by the track,
        - dialog / cutscene timing synchronized without dead-reckoning
          frame counts.
- [ ] **MIDI CC parsing** — CC 7 (Volume), CC 10 (Pan), CC 11 (Expression),
      Pitch Bend. Parser currently skips them; would let authored MIDI
      mix automation drive runtime per-channel volume / pan / pitch.
- [ ] `Music.GetBar()` — query bar number on top of `GetBeat`.
- [ ] **MIDI event-marker authoring UI** — either a timeline overlay or
      a plain string list ("emit event 'chorus_start' at beat 128").
      Minimum viable is the string list; timeline view is Phase 3 polish.
- [ ] **MIDI interpreter bug audit.** The 2026-04-21 1/3/5→0/2/4
      `MidiChannel` scene fix (commit 6ce9621) hints at more off-by-one
      or range bugs in the chain. Systematic pass over `MidiParser.cs`
      (status-byte masking, running-status handling, delta-tick
      accumulation, tempo meta 24-bit read), `PS1MSerializer.cs`
      (channel filtering, note-range inclusivity, NoteOff-before-NoteOn
      sort stability, drum-kit route resolution), and the PSX-side
      `musicsequencer.cpp` (event dispatch, voice allocation, pitch
      table indexing). Fixture test: one `.mid` per suspicious edge
      case (format 0 vs. 1, running status, 14-bit pitch bend, tempo
      mid-track, drum kit with out-of-range note) round-tripped
      through the exporter and diffed against an authoritative reader.
      High priority — bugs here silently produce wrong music.

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
- [ ] **`TrackType::ObjectScale` cutscene / animation track.** Authored
      as a Vector3 per keyframe (uniform or per-axis). Runtime expands
      the GameObject's 3×3 to include scale diagonal before it feeds
      the GTE. Use cases: doors opening, pulsing pickups, squash-and-
      stretch. Discord feature request "Object scale animation"
      (psxsplash channel, 2026-04, 2 upvotes, "considered").
      **[runtime]**

### Fixed-camera presets (`PS1FixedCamera` node)

For CCTV / Resident-Evil-style scenes with N pre-authored camera angles
the player or script switches between. Authoring a fixed cam today is a
3-bug obstacle course (`docs/fixed-camera-authoring.md` is the full
write-up): manual PSX coord conversion, `Camera.LookAt` is a no-op stub,
and psyqo's pitch sign is inverted vs author intuition.

- [ ] **`PS1FixedCamera.cs` Tool node** alongside `PS1Room.cs`. Authored
      in Godot world space with optional `LookAtTarget` Node3D ref and
      `ProjectionH` int. Editor gizmo previews the runtime FOV at the
      authored position so framing is WYSIWYG.
- [ ] **`SceneCollector.CollectCameras`** bakes each node into a
      `CameraPresetRecord` with PSX-frame position + sign-corrected
      pitch/yaw + projH + name + optional shake config. New splashpack
      block; bumps version.
- [ ] **`Camera.LoadPreset(name_or_index)` Lua API** in psxsplash applies
      position + rotation + H + shake from the baked record. Ships
      alongside the splashpack version bump. **[runtime]**
- [ ] Migrate `monitor.tscn` (jam game) off the hand-tuned `FEEDS`
      table to four `PS1FixedCamera` nodes once the above lands. That
      proves the migration story.

Bug-source memories that informed this: `project_camera_lua_coord_frame.md`,
`project_camera_lookat_stub.md`, `project_camera_pitch_sign.md`. Adding
the node prevents all three from biting future authors.

### Camera modes (1st/3rd/Orbit)

`PS1Player.CameraMode` already ships as an authoring-side enum, but the
runtime hardcodes a single third-person rig. Wire the mode into the
runtime so first-person / orbit actually take effect.

- [ ] Splashpack header carries the chosen mode (one u8 byte).
- [ ] `Camera.SetMode(mode)` Lua API so the options menu can switch at
      runtime. **[runtime]**
- [ ] First-person mode: camera locked at player head height
      (`playerPosition + (0, playerHeight, 0)`), forward = player facing,
      player mesh hidden. **[runtime]**
- [ ] Third-person mode: camera trails at an authored offset (default
      `(0, playerHeight * 1.2, 3.0)`). PS1Player's Camera3D child
      supplies the offset when present. **[runtime]**
- [ ] Orbit mode: right-stick rotates camera around player pivot, radius
      authored on PS1Player. **[runtime]**
- [ ] **`Camera.SetPosition/SetRotation` Lua override.** Lets a Lua
      script drive the camera each frame without needing a new built-in
      mode — over-the-shoulder aim, cinematic lock-ons, free-look
      photo modes, the Wind Waker-style "let go of the stick, camera
      glides back." Runtime reads player rig if no override was set
      this frame. Pairs with `Scene.SetPaused(true)` for photo modes.
      Discord feature request "Other controller types or lua based
      camera control" (psxsplash channel, 2026-04). **[runtime]**

### Texture animation (UV scroll + frame-flip)

Highest visual return per cycle on PS1: water rippling, conveyors,
mouths flapping, eyes blinking, scrolling skyboxes. Two cheap
mechanisms cover most cases — UV-shifting and atlas-region cycling.

- [ ] **UV-scroll track type** — `TrackType::ObjectUVScroll` adds a
      per-frame UV offset (`du`, `dv` in fp12) to all of an object's
      triangles. Emit as a new track in PS1Animation / PS1Cutscene with
      values `(scrollSpeedU, scrollSpeedV, 0)`. Authored as a Vector2
      in pixels-per-second; exporter converts. **[runtime]**
- [ ] **Atlas-region flip** — `TrackType::ObjectFrameSwap` swaps
      between authored UV rectangles within a single texture page.
      Each keyframe selects a frame index; the runtime rewrites U/V
      offsets on the object's triangles each tick. Use case: blinking
      eyes, mouth shapes for dialog, animated water tiles. Frame
      regions authored as a list on PS1MeshInstance (e.g.
      `Vector2I[] FrameRects` in atlas pixels). **[runtime]**
- [ ] **Per-mesh UV anim mode** on `PS1MeshInstance` so you can mark a
      mesh as "use UV scroll" without writing a per-mesh Lua script.
- [ ] Demo addition: animated water plane (UV scroll) + a face mesh on
      an NPC with mouth-flap atlas frames (frame swap).

### Rendering options

Visual / rendering features authors ask for that don't fit into the
more specialized sub-themes. Each is mostly runtime-side work with a
~1–2-property authoring surface; aggregated here so we can knock them
off together instead of one-off branches. Sources: Discord
`#feature-requests`, psxsplash channel, 2026-04.

- [ ] **Sprite / billboard objects.** `PS1Sprite` node — a single-quad
      mesh that always faces the active camera. Exporter emits it like
      any other mesh; the runtime rotates the quad's basis each frame
      to align with `Camera.GetForward()` (or the camera-to-sprite
      vector for full-axis billboards). Cheap (2 tris, one basis
      update per sprite). Use cases: foliage, pickups, ground shadow
      blobs, particle stand-ins before a full particle system.
      Discord ask "Sprite objects" (2 upvotes). **[runtime]**

- [ ] **Per-mesh backface rendering toggle.** `PS1MeshInstance.DoubleSided`
      (bool, default false). When set, exporter either duplicates each
      triangle with reversed winding or the runtime skips the `nclip`
      back-face reject for objects flagged double-sided. Use cases:
      single-sided foliage / banner planes, thin signs, interior
      wallpapers — currently invisible from one side. Discord ask
      "Render both sides of a tri option" / "Render BOTH faces, not
      just front." **[runtime]**

- [ ] **LOD meshes per `PS1MeshInstance`.** `LODs` = ordered
      `(Mesh, distanceMeters)[]`. Exporter packs every LOD into the
      atlas; runtime swaps by distance to camera with a small
      hysteresis band. PS1 poly budgets are small enough that a naïve
      two-or-three-step swap is all anyone actually wants. Discord ask
      "LODs" (2 upvotes). **[runtime]**

- [ ] **2D parallax skybox.** `PS1Sky` node referencing 1–3 layered
      textures with per-layer parallax factors (0 = sky dome locked to
      the horizon, 1 = world-locked). Renderer draws them as
      full-screen layered quads **before** scene geometry so they
      compose under everything. Authentic technique (Crash, Spyro,
      MediEvil); far cheaper than a real skybox cube. Discord ask "2D
      texture based skyboxes" (2 upvotes). **[runtime]**

- [ ] **Subtitle helper on `UI.SetText`.** Document (and verify
      against a minimal repro) that `UI.SetText` fires correctly
      during `Cutscene.Play`. Ship a `Subtitle.Show(text,
      durationFrames)` convenience that drives a reserved dialog
      canvas during cutscenes. Discord ask "add a way to change text
      of a PSX UI Text in cutscene."

### UI / HUD from Lua

Canvas assets export; runtime mutation doesn't.

- [ ] `UI.SetText(canvas, slot, str)` — dialog, inventory counts, health.
- [ ] `UI.SetColor(canvas, slot, r,g,b)` — flash-on-damage.
- [ ] `UI.Show/Hide(canvas)`, `UI.SetImage(canvas, slot, atlasIdx)` for
      animated icons.

### UI authoring experience *(Phase 3 polish, highest-leverage items)*

The current authoring flow (PS1UICanvas + PS1UIElement with absolute
X/Y/W/H numbers) works but the feedback loop is "edit numbers → run
on PSX → see what's wrong → guess → repeat." Authors who aren't used
to eyeballing 320×240 pixel positions bounce off this hard. The items
below follow the UI/UX plan tenets (intuitive, non-intimidating,
modern, beautiful) — see `docs/ui-ux-plan.md` § UI authoring.

- [ ] **WYSIWYG preview in Godot's 2D viewport.** When a PS1UICanvas
      is selected, render its elements at 320×240 reference scale with
      a bordered frame showing the PS1 screen. Drag handles for
      X/Y/W/H; inspector updates live. Uses Godot's existing 2D tools
      — elements are rendered by a custom `_Draw()` in the canvas
      node during editor mode.
- [ ] **Anchors + alignment on PS1UIElement.** `Anchor = TopLeft |
      TopCenter | TopRight | Center | BottomLeft | BottomCenter |
      BottomRight` — computed at export time so the binary contains
      absolute X/Y as today. Plus text alignment (left / center /
      right). Replaces the "count pixels, hope for the best"
      workflow with one-click placement against the PS1 screen edges.
- [ ] **Auto-wrap text on Width.** Element Width already exists but
      isn't used as a wrap column. Exporter measures each word
      against the font's `advanceWidths`, inserts `\n` at word
      boundaries to fit. Authors type a paragraph; exporter handles
      layout. Keeps the explicit `\n` feature for when authors want
      dramatic breaks.
- [ ] **Dialog tree editor (custom EditorPlugin dock).** Node-based
      graph for branching dialog. Each node: text + choices + optional
      conditions (`QuestFlag.Has("met_NPC")`) + optional side-effects
      (`QuestFlag.Set(...)`). Arrows connect choices to next nodes.
      Saves as a .tres resource; exporter compiles to a dialog
      bytecode block in the splashpack. Runtime API:
      `Dialog.Start(treeName)`, `Dialog.Choose(choiceIdx)`,
      `onDialogEnd(treeName)`. Replaces every game's hand-rolled
      state machine over `UI.SetText + Input.IsPressed`.
- [x] **PS1 UI prefab templates.** Ship common building blocks
      (`addons/ps1godot/ui_templates/`):
        - `dialog_box.tscn` — background + body text + name tag.
        - `menu_list.tscn` — title + 4 items + cursor.
        - `hud_bar.tscn` — label + fill bar + background.
        - `toast.tscn` — floating notification.
      Authors drop one onto a canvas, tweak strings, done.
      **Landed 2026-04-20.** 9-patch-bordered dialog + portrait slot
      deferred to the 9-patch bullet below.
- [x] **PS1 UI theme resource (`PS1Theme.tres`).** Central 8-slot
      palette (Text / Accent / Bg / BgBorder / Highlight / Warning /
      Danger / Neutral). Each element opts in via `PS1UIThemeSlot`
      enum (`Custom` keeps authored color). Change theme once →
      every opted-in element restyles. Resolution happens at export
      time, so no runtime format change. **Landed 2026-04-20.**
- [x] **3D-model HUD widgets (`PS1UIModel`).** Splashpack v23 adds a
      ui-model-table parallel section. Author drops a `PS1UIModel` child
      under a `PS1UICanvas`, sets Target (NodePath to a PS1MeshInstance),
      screen rect, and orbit yaw/pitch/distance/projectionH. Renderer
      adds a post-main-scene HUD pass that swaps the camera matrix to a
      per-model orbit transform and re-renders that GameObject's polys
      on top. Lua: `UI.SetModelVisible(name, bool)`,
      `UI.SetModelOrbit(name, yawPi, pitchPi[, dist])`,
      `UI.SetModel(name, goName)` for inventory-style target swap.
      Static meshes only in v1 (skinned meshes deferred — polys live on
      SkinAnimSet, not on the GO). No scissor clipping — author's camera
      framing is expected to keep the model within its declared rect;
      per-primitive clip is a follow-up if bleed proves problematic.
- [ ] **9-patch border UI element.** A new `PS1UIElementType.Border`
      that takes corner + edge texture references and renders a
      scalable panel. Dialog boxes + HUD frames stop requiring manual
      stacking of Box elements.
- [ ] **Higher-level Lua helpers.** Today's `UI.SetText` +
      `UI.SetCanvasVisible` + `Input.IsPressed` dance gets wrapped
      into:
        - `Dialog.Show({ text = "...", portrait = "narrator",
          choices = { "Yes", "No" } })` → returns choice index.
        - `Toast.Show(text, durationSeconds)` → auto-hide.
        - `Menu.Pick(items, { cursor = "pointer" })` → blocks until
          a pick (cooperating with Scene.SetPaused).
      All built on the existing primitives but with dialog/menu
      semantics baked in.
- [ ] **Portraits / character tags.** A `PS1UIPortrait` element type
      pointing at an atlas region + a `name` string slot. Dialog
      prefab auto-wires them so "who's talking" is one authored
      reference, not per-line `UI.SetImage` bookkeeping.
- [ ] **Live PS1-quantized preview.** Render the authored UI through
      a shader that applies the PS1 font's exact glyph metrics,
      15-bit color clamp, and dithering in the editor — WYSIWYG
      matching what PSX will show pixel-for-pixel, not Godot's
      default anti-aliased preview.
- [ ] **Canvas residency linter.** Warn when a `Gameplay` canvas
      references an element whose assets aren't marked gameplay-
      resident, and vice versa. Catches "oh right, this menu icon
      doesn't load during gameplay" at author time.

### Scene queries, tags, messaging

- [ ] `GameObject.SetTag/GetTag(self, tag)` (uint16 on the struct).
- [ ] `Scene.FindByTag(tag)` → list; `Scene.FindNearest(pos, tag)`.
- [ ] `Entity.Send(target, "event", args...)` — custom event dispatch,
      observer pattern without polling.
- [ ] `Scene.GetSharedState() → table` — scene-scoped state that all scripts
      see (currently each script sandboxes against `_G` gymnastics).

### Input

- [ ] `Input.IsPressed(pad, btn)` — multi-pad; current API implicitly pad 0.
- [x] ~~`Input.JustPressed(btn)`~~ — already shipped as `Input.IsPressed`,
      which is the single-frame press edge (confusingly named: the continuous
      "held" variant is `Input.IsHeld`). Roadmap was wrong; closing as-is.
- [ ] `Input.SetRumble(pad, intensity, frames)` — analog controllers support it.

### Cutscenes / flow control

Runtime has cutscene playback; Lua can't trigger it.

- [ ] `Cutscene.Play(name)` / `Stop()` / `IsPlaying()`.
- [x] `Scene.PauseFor(frames)` — timed hit-stop freeze. Holds animation /
      cutscene / skin / collision / Lua onUpdate while keeping render +
      camera shake + music alive. Souls/Hades-style impact crunch. Stacks
      via `max(remaining, requested)`.
- [ ] `Scene.SetPaused(bool)` — indefinite freeze for menus / inventory
      (PauseFor is the timed variant; this is the toggle).
- [x] `Camera.Shake(intensity, frames)` — random per-frame camera jitter
      with linear decay. Pairs with `Scene.PauseFor` for impact feedback.

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

### Authoring framework gaps *(from 2026-04-21 source survey)*

A 2026-04-21 sweep of UE 5.7 / psyqo / SplashEdit / psxsplash / Godot
for borrowable gameplay primitives picked three as high-leverage and
shippable under PS1 budgets. Full reasoning in memory
`project_authoring_survey_2026_04_21.md`; deferred items listed at the
bottom so they're logged, not lost.

- [ ] **Input actions + contexts.** Replace raw
      `Controls.isButtonHeld(TRIANGLE)` polling with named actions
      ("Interact", "Move", "Jump") bundled into contexts ("playing",
      "menu", "dialog", "cutscene") that activate mutually-exclusively.
      State-stack transitions (below) swap contexts automatically, so
      cutscenes silence gameplay input without every script remembering
      to check `isPaused`. Author declares actions via a
      `PS1InputMap.tres` resource; exporter packs a small action table
      (~32 B of runtime state — per-action bitmask + two int8 analog
      channels + modifier enum). Supersedes the `Input.IsPressed /
      JustPressed` items above — those remain the low-level accessors
      the action layer sits on top of. Inspired by UE Enhanced Input,
      shrunk. **[runtime]**

- [ ] **Game state stack.** Formalize "what is the game doing right
      now" as a push/pop stack of named states (Loading, Playing,
      Paused, Cutscene, Dialog, Menu). Each state owns its input
      context, audio ducking target, and UI visibility. Pushing Dialog
      over Cutscene suspends the Cutscene tick without tearing it
      down; popping resumes. Answers the recurring "how do I pause?"
      / "can I layer a dialog over a cutscene?" questions without Lua
      globals. Builds on the psyqo Scene-stack primitive we currently
      underuse. `Game.PushState(name)` / `Game.PopState()` Lua API;
      transitions emit `onStateEnter(name, reason)` /
      `onStateExit(name, reason)` callbacks. **[runtime]**

- [ ] **Camera rig abstraction with blending.** Decouple camera config
      from `PS1Player`: `CameraRig` is a ~32-byte struct (position
      offset `Vec3`, follow speed fp12, projH int16, mode enum) and
      `CameraManager` blends between rigs over N frames via
      `Camera.BlendTo(rig, frames, easing)`. Rooms, triggers, and
      cutscenes declare a preferred rig; switching is a data change,
      not a code branch. Unlocks Resident Evil-style fixed-angle room
      cameras, cinematic rail cameras, and smooth cuts between them
      without any Lua camera-math loops. Subsumes the "Camera modes
      (1st/3rd/Orbit)" section above — those become specific built-in
      rigs, not three separate code paths. **[runtime]**

**Deferred for now, logged for future signals:**

- *Pawn / controller / movement-component split (UE-style).*
  Architectural win but premature without a second controller use case
  (AI-possessed enemies, spectator cam). Revisit when one emerges.
- *Event bus / delegate publish-subscribe.* Good shape but overlaps
  with coroutines (Phase 2.5 above) and `Entity.Send` messaging;
  low net gain right now.
- *Ability struct (cooldown + cost + can-activate).* Phase 2.6 covers
  a richer version via GAS-lite — no need for a lean duplicate here.
- *Movement modes (Walk/Fall/Swim/Climb).* Useful but every game wants
  its own mode set; better authored per-game in Lua on top of
  `Physics.Raycast` + coroutines.
- *Nav layer bitmasks.* Worthwhile once non-flat nav (Phase 2 bullet 7)
  lands; depends on that data shape.
- *Animation parameter caching (playhead / remaining / will-end).*
  Small API surface; add when the first real script asks for it.

**Cross-cutting north star (not a bullet, a reminder):** both UE and
Godot expose movement, input, and interaction as *components* that
plug onto a minimal entity. PS1Godot should trend in this direction in
Phase 2.6+ rather than pile features onto `PS1Player`. Don't refactor
preemptively, but when a second use case appears for the same feature,
prefer extracting a component over parameterizing the host node.

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
      **Amendment (`REF-GAP-1` / `REF-GAP-2` / `REF-GAP-3`):** framed around
      *per-scene residency* (environment / character / UI / effects broken
      out), not project totals. Includes texture-page grouping view and
      OT-pressure readout (object count vs `PS1Scene.MaxActors`,
      texture-page switches per frame vs `MaxTexturePages`).
- [ ] **SPU / memory / BVH budget bars** in the viewport overlay.
- [ ] **Texture reuse auditor (`REF-GAP-6`).** Warn on one-off textures,
      near-duplicate CLUTs that could merge, and meshes that each drag in a
      unique atlas. Powered by the data `SceneCollector` + `VRAMPacker`
      already have.
- [ ] **UV out-of-range linter.** Warn when any vertex UV falls outside
      `[0, 1]` — the PSX rasteriser doesn't wrap/clamp, so out-of-range
      UVs pull whatever atlas neighbor happens to sit at that tpage
      offset (usually garbage). Exporter already walks every mesh's
      UVs; one bounds check per vertex. Option to auto-wrap `(u % 1)`
      with a warning when the mesh is tagged "intended-tiling".
      Discord ask "Fix UVs of meshes when exporting if the UV
      coordinates are too large" (2 upvotes).
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
      **Amendment (`REF-GAP-10`):** disc-layout-aware. Place adjacent-chunk
      archives physically close on the disc to reduce seeks. Only matters
      once Phase 2.5 chunk streaming is real.
- [ ] Loading screens (uses existing PSXCanvas path; just a convention).

### Graph authoring framework (PS1Graph)

Visual node-graph authoring is the right shape for a whole family of
concerns, not just one. Rather than build a single "visual scripting"
feature, ship **one graph framework** — editor dock, pin model,
serialization, compiler seam — and let each concern register as a
*graph kind* with its own node palette and compiler pass. Inspired by
Unreal's Blueprint machinery (`FKismetCompilerContext` → backend swap),
but compiles to Lua at **export time** — runtime carries zero VM or
graph-walker overhead.

- [ ] **D0. PS1Graph framework.** `PS1GraphResource` (.tres) +
      `PS1GraphNode` + `PS1GraphConnection` base classes; editor dock
      built on Godot's `GraphEdit`. Typed pin system (exec + data),
      context-menu palette, live validation, search.
- [ ] **D1. `PS1DialogueGraph`** — *first graph kind.* Nodes: Line,
      Choice, Condition, SetFlag, GiveItem, PlaySound, StartCutscene.
      Compiles to a small Lua table (nodes + edges) walked by a stock
      `Dialog.RunGraph(name)` helper. Replaces the Phase 3 "Dialog
      tree editor" bullet above — same feature, unified framework.
- [ ] **D2. `PS1QuestGraph`** — objectives as nodes, prerequisites as
      edges, branch outcomes (success / fail paths). Compiles to a
      quest state machine with save/load integration
      (`QuestFlag.Set/Has`). `Quest.*` Lua API.
- [ ] **D3. `PS1FSMGraph`** — states + transitions. Replaces the
      hand-written `StateMachine.new({...})` from Phase 2.5 AI with
      visual authoring. Same Lua output shape, different front end.
- [ ] **D4. `PS1ScriptGraph`** — general-purpose Blueprint-style
      scripting for trigger logic and event reactions. Last in order
      because the node palette is unbounded and the use cases are
      vaguer until D1–D3 land first. **Reject** from UE Blueprint:
      UObject-per-node (use `Resource` subclasses), wildcard pin types
      (fixed set: bool / int / fp12 / string / vec3 / entity-ref),
      runtime VM (compile to Lua, zero runtime cost).

Done when an author can open a graph editor dock, pick "New Dialogue
Graph" / "New Quest Graph" / etc., author nodes + connections
visually, hit Run on PSX, and see their dialogue / quest / FSM run
without writing Lua by hand.

### Music authoring experience

Phase 2.5 shipped sequenced music end-to-end (drop a `.mid` + sample
WAVs, bind by hand, play on PSX). The MVP works but the binding step
is "open inspector, fill 6+ resource fields per channel, hope the
mix is right." We learned a lot doing this for the demo song —
several of those discoveries should become first-class plugin
features so other authors don't repeat the same trial-and-error.

Tiered by effort vs. user reach:

**Tier 1 — DAW-agnostic (works for any DAW that exports MIDI):**

- [ ] **`PS1MusicSequence` "Import MIDI" inspector button.** Pick a
      `.mid`, the plugin parses it (re-using the same `MidiParser` the
      exporter calls), and auto-creates a starter `PS1MusicChannel`
      sub-resource per MIDI channel found in the file. User still
      drops in samples + tunes mix, but the per-channel binding
      skeleton is filled in for them. Massive reduction in
      "what-do-I-bind-to-what" friction.
- [ ] **MIDI inspection / debug tools committed under `tools/`** —
      `midi_inspect.py`, `midi_polyphony.py` already shipped from
      the demo work. Document them in the music guide so authors
      can sanity-check their `.mid` (per-track polyphony, channel
      distribution, note ranges) before/after import.
- [ ] **"Music Authoring Guide"** at `docs/music-authoring.md`.
      Universal flow ("export multi-track .mid + render each
      instrument as a single short WAV at a known root note"), one
      paragraph per major DAW (Reaper, Ableton, FL, Logic) on how
      to do the per-channel render in that specific DAW.
- [ ] **MIDI event-marker authoring** for `Music.OnEvent` — minimum
      viable is a string list ("emit 'chorus_start' at beat 128") on
      the `PS1MusicSequence` resource; timeline overlay is later
      polish. Requires the runtime side to land first
      (`onMusicEvent` Lua callback — listed above in Phase 2.5).

**Tier 2 — Reaper-specific power feature:**

- [ ] **`tools/rpp_extract_samples.py` upgrade.** Already extracts
      sample paths + per-track linear volumes (proven on
      `RetroAdventureSong.rpp`). Extend to also pull
      ReaSamplOmatic5000 root notes (one of the doubles in the VST
      state) and ReaSynth params (waveform + ADSR for chord
      tracks). Emit a JSON the editor can consume.
- [ ] **`PS1MusicSequence` "Sync from Reaper project" button.** Picks
      a `.rpp`, runs the extractor, builds out a complete sequence
      resource: sample paths copied + trimmed to fit the SPU budget,
      base notes set, per-channel volumes mapped from Reaper's mix,
      drum-kit splits inferred from MIDI note distribution. The
      author clicks "Run on PSX" and the song plays approximately
      as authored, no manual binding step.
- [ ] **Sample auto-conversion** — the extractor already needs to
      downmix stereo→mono, resample to a PSX-friendly rate, and
      trim to fit the 508 KB SPU. Bake that into a reusable
      utility (`tools/convert_real_samples.py` is the
      starting point) so it's available for any sample-bank import,
      not just Reaper projects.
- Reaper-only because the `.rpp` is plain text and decodable;
  Ableton's `.als` / FL's `.flp` are binary and would need separate
  parsers (much bigger investment).

**Tier 3 — Universal stem-render flow** *(later polish)*:

- [ ] **Per-track stem-render convention.** Author renders each track
      as audio in their DAW (every DAW supports this), names the
      output `track_bass.wav` / `track_lead.wav` / etc. next to the
      `.mid`, and the importer auto-binds each MIDI track to the
      matching audio file by name. Works for any synth in any DAW
      because the user is rendering through their actual instrument
      patches — sample fidelity matches the source perfectly.
- [ ] **Pitch-recognition assist** — when an author drops a stem
      WAV, optionally analyze it for fundamental frequency at the
      first audible note and suggest a `BaseNoteMidi` value. Cheap
      via FFT or autocorrelation. Saves the "what note was this
      sample recorded at?" guesswork.

**Done when:** an author who's never seen PS1Godot can drop a `.mid`
+ a folder of WAVs into a scene, click an Import button, get a
working sequence resource that plays a recognizable rendition of the
source on PSX with no manual binding work.

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
