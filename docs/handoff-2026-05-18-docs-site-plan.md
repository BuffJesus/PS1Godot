# Handoff — v0.5 ship + docs-site plan (2026-05-18)

This session shipped v0.4.0 (UE port batch verification + Option 1
polish + 6 follow-up fixes) and v0.5.0 (Linux + macOS support via
cross-platform Python launcher + GDExtension matrix + per-OS docs).
The next user-facing friction is **plugin discoverability**: as the
surface area grew, "I don't know where to start" became the biggest
new-user barrier. Upstream psxsplash uses [Material for
MkDocs](https://psxsplash.github.io/docs/latest/); we're targeting the
same framework so anyone landing from upstream sees a familiar shape,
plus we get built-in client-side search for free.

This doc is the playbook for the next session. Walk it top to bottom:
session context → CI verification → docs-site plan → screenshot
capture checklist → restructure mapping. **The capture checklist is
the part requiring user input** — every other slice is mechanical.

---

## Session context summary

**Pushed to `origin/main` this session:** 9 commits for v0.4.0
(`63d14c6..dd045ae`), then 4 commits for v0.5.0 stages 1-4
(`58f26d0..0e035ec`), then 4 CI-fix follow-ups (`2a7db6c..ab15059`).
Two tags pushed: `v0.4.0`, `v0.5.0`.

**v0.4.0 highlights** (already released, artifacts in `dist/`):
- ScrollContainer fix for the right-side dock.
- Compile button writes sibling .lua.
- Disabled-entry auto-promotes downstream.
- EnabledState survives reload round-trip (ReconstructGraphFromBareResource fix).
- Title tints with luminance-based text contrast.
- Bottom-panel consolidation 10 → 4 tabs.
- Dialogue typewriter reveal.
- ASCII-only printfs (no more PCSX-Redux mojibake).
- PS1LuaScript `_editor_can_reload_from_file` override.
- .tres save auto-syncs sibling .lua via FilesystemChanged.
- PS1AudioClip waveform thumbnails (small-preview overrides).
- `take_over_path` on .lua reload (no more cyclic-resource warnings).
- Submenu QueueFree guarded by IsInstanceValid.
- GC.Collect drain in _ExitTree (kills the post-shutdown 0xc0000005).

**v0.5.0 highlights** (tag pushed, artifacts in `dist/`, GitHub Release
not yet published):
- `scripts/run.py` cross-platform launcher dispatches 5 actions.
- `.cmd` + `.sh` shims wrap it on each platform.
- `PS1GodotPlugin.RunScript` + two CreateProcess sites refactored.
- `ps1lua.gdextension` declares Linux + macOS library entries.
- `build-release.py` packages both PCdrv + CDROM runtimes; auto-reads
  splashpack version from the runtime's own assertion.
- GitHub Actions matrix (`.github/workflows/build-gdextension.yml`):
  3 OS × 3 targets for GDExtension, parallel C# plugin build job.
- `.gitattributes` pins .sh/.py to LF, .cmd to CRLF.
- SETUP / QUICKSTART / README updated for cross-platform.

## CI verification (open item)

The CI matrix has been the source of the most recent commits since the
push. Status as of session end:

| Run | SHA | Result |
|---|---|---|
| #1 | `0e035ec` | 12/12 fail — pre-pin baseline |
| #2 | `0e035ec` (tag) | 12/12 fail — same SHA |
| #3 | `2a7db6c` | 2/3 C# pass (Linux + macOS), 1/3 Windows C# fail (POSIX path), 9/9 GDExt fail (fetch-by-SHA) |
| #4 | `4b31eb5` | 3/3 C# pass (`pwd -W` fix), 9/9 GDExt fail (wrong-branch hint) |
| #5 | `732d8cd` | 3/3 C# pass, 9/9 GDExt fail (SHA d328849 unreachable) |
| #6 | `ab15059` | (pending at session end) — pinned to real upstream SHA 4862a9d |

**Root cause of the GDExtension failures:** my SHA pin (`d328849`)
was the local working-tree HEAD, which sits one private commit on top
of upstream master. GitHub returns 422 "No commit found" for that SHA.
`ab15059` repins to the merge-base (`4862a9d`) which exists upstream
and is functionally equivalent (the local commit is a formatting
tweak only).

**Verification step for next session:**

```bash
curl -s "https://api.github.com/repos/BuffJesus/PS1Godot/actions/workflows/279217565/runs?per_page=1" | python -c "
import json, sys
r = json.load(sys.stdin)['workflow_runs'][0]
print(f'Run #{r[\"run_number\"]} sha={r[\"head_sha\"][:7]} status={r[\"status\"]} conclusion={r[\"conclusion\"]}')
"
```

If Run #6 is green: download the per-OS GDExtension artifacts, drop
into `godot-ps1/addons/ps1godot/scripting/build/`, re-run
`python scripts/py/build-release.py v0.5.1`, tag + push `v0.5.1`,
publish the release with cross-platform binaries finally bundled in
the plugin zip.

If Run #6 is still red: read the failed-at step name (anon API gives
that without auth). Logs are 403 anonymous — user pastes from the
GitHub UI.

---

## Docs-site plan

**Framework:** Material for MkDocs. Same as upstream psxsplash. Free,
markdown-driven, client-side lunr search, GitHub Pages deploy via
Actions.

**Sliced delivery:**

1. **Scaffold + restructure (1-2 commits).** Add `mkdocs.yml`, theme
   config, GitHub Pages deploy workflow. Move existing `docs/*.md`
   into the new nav structure (see "Restructure mapping" below).
   Goes live at `buffjesus.github.io/PS1Godot/` on first push to main
   after the workflow lands.
2. **Lua API auto-generator (1 commit).** Python script parses
   `addons/ps1godot/scripting/api/lua_api.lua`'s EmmyLua annotations
   (`---@param`, `---@return`, function signatures, leading comment
   blocks) into per-namespace Markdown pages under `docs/lua-api/`.
   Re-runs as a build step (or pre-commit hook).
3. **Worked examples (1 commit).** Second pass: grep every `.lua` in
   the project (demo, smoke fixtures, anything else committed) for
   each API call, pick the cleanest usage, embed as a snippet in the
   reference page. After this, `Audio.PlaySfx`'s reference page shows
   both the signature AND a snippet like `Audio.PlaySfx("door_creak")`
   from `lua_snippet.lua` with a note about where it's from.
4. **Hand-written guides (multi-commit, ongoing).** Per-node guide,
   per-dock guide, getting-started tour, troubleshooting FAQ. This is
   where the screenshot capture checklist below feeds in. Don't try
   to ship all of these at once — pick the 3-5 highest-value pages
   first (anything new authors hit in the first 30 minutes), ship,
   iterate.

**Skipped for now:** C# auto-reference. Programmers reading source
already get XML doc tooltips in their IDE; building a full DocFX-style
HTML reference is fiddly and low-leverage. Revisit in v0.6+ if a real
contributor asks for it.

---

## Restructure mapping — existing `docs/*.md` → new MkDocs layout

The new structure:

```
docs/
├── index.md                          # landing page (replaces README front)
├── getting-started/
│   ├── installation.md               # ← SETUP.md
│   ├── quickstart.md                 # ← QUICKSTART.md
│   ├── first-scene.md                # ← tutorial-hello-cube.md + tutorial-basic-scene.md merged
│   └── troubleshooting.md            # NEW — write from session diaries
├── authoring/
│   ├── nodes/
│   │   ├── ps1-scene.md              # NEW — handwritten
│   │   ├── ps1-mesh-instance.md      # NEW
│   │   ├── ps1-skinned-mesh.md       # NEW
│   │   ├── ps1-camera.md             # NEW
│   │   ├── ps1-player.md             # NEW
│   │   ├── ps1-animation.md          # NEW (anchor: docs/fixed-camera-authoring.md)
│   │   ├── ps1-cutscene.md           # NEW
│   │   ├── ps1-audio-clip.md         # NEW (anchor: docs/ps1-audio-routing.md)
│   │   ├── ps1-music-channel.md      # NEW (anchor: docs/sequenced-music-format.md)
│   │   ├── ps1-trigger-box.md        # NEW
│   │   ├── ps1-ui-canvas.md          # NEW
│   │   ├── ps1-ui-element.md         # NEW (anchor: docs/ui-ux-plan.md)
│   │   ├── ps1-room.md               # NEW
│   │   ├── ps1-portal-link.md        # NEW
│   │   └── ps1-sky.md                # NEW
│   ├── graphs/
│   │   ├── overview.md               # NEW (anchor: docs/ps1graph-*.md as group)
│   │   ├── dialogue.md               # ← docs/ps1graph-dialogue-authoring.md
│   │   ├── fsm.md                    # ← docs/ps1graph-fsm-authoring.md
│   │   ├── quest.md                  # ← docs/ps1graph-quest-authoring.md
│   │   ├── behavior-tree.md          # ← docs/ps1graph-bt-authoring.md
│   │   └── script-graph.md           # NEW (D4 future work)
│   ├── audio/
│   │   ├── routing.md                # ← docs/ps1-audio-routing.md
│   │   ├── sound-banks.md            # ← docs/sound-macro-plan.md
│   │   └── sequenced-music.md        # ← docs/sequenced-music-format.md
│   └── ui/
│       ├── canvas.md                 # ← docs/ui-ux-plan.md (relevant parts)
│       ├── custom-boot-logo.md       # ← docs/custom-boot-logo.md
│       └── splashedit-import.md      # ← docs/psx-asset-swap-guide.md (if relevant)
├── docks/
│   ├── overview.md                   # NEW — screenshot of strip + index
│   ├── ps1godot-panel.md             # NEW — main right-side dock
│   ├── graph.md                      # NEW — PS1 Graph
│   ├── doctor.md                     # NEW — PS1 Doctor
│   ├── authoring-tools.md            # NEW — PS1 Authoring container
│   ├── tools.md                      # NEW — PS1 Tools container
│   ├── quest-journal.md              # NEW (sub-page detail)
│   ├── graph-find.md                 # NEW
│   ├── references.md                 # NEW
│   ├── lua-repl.md                   # NEW
│   ├── ui-canvas.md                  # NEW
│   ├── vram-viewer.md                # NEW
│   ├── audio-routing.md              # NEW
│   └── lua-cheatsheet.md             # NEW
├── lua-api/
│   ├── overview.md                   # NEW
│   ├── audio.md                      # AUTO from lua_api.lua
│   ├── camera.md                     # AUTO
│   ├── dialog.md                     # AUTO
│   ├── debug.md                      # AUTO
│   ├── fsm.md                        # AUTO
│   ├── quest.md                      # AUTO
│   ├── bt.md                         # AUTO
│   ├── persist.md                    # AUTO
│   ├── music.md                      # AUTO
│   ├── scene.md                      # AUTO
│   └── ui.md                         # AUTO (or per-namespace as lua_api.lua dictates)
├── reference/
│   ├── splashpack-format.md          # ← docs/splashpack-format.md
│   ├── psxsplash-improvements.md     # ← docs/psxsplash-improvements.md
│   ├── budgets.md                    # NEW — VRAM / SPU / triangle / Tex Page caps
│   ├── known-issues.md               # NEW
│   ├── glossary.md                   # ← GLOSSARY.md
│   └── api-showcase.md               # ← docs/api-showcase.md
├── contributing/
│   ├── architecture.md               # NEW — cross-cuts CLAUDE.md without leaking
│   ├── building.md                   # NEW — gdextension + plugin + runtime
│   ├── adding-a-node-kind.md         # NEW
│   ├── adding-a-graph-kind.md        # NEW
│   └── ci.md                         # NEW — explain the build-gdextension matrix
└── internal/                          # NOT in MkDocs nav — design docs / RFCs only
    ├── archive/                       # ← docs/archive/
    ├── rfc/                           # ← docs/rfc/
    ├── projects/                      # ← docs/projects/
    ├── handoff-*.md                   # ← all docs/handoff-*.md
    ├── 00-overview.md                 # ← (if exists)
    ├── 02-architecture.md             # ← (if exists)
    ├── ps1godot-lighting-plan.md      # ← docs/ps1godot-lighting-plan.md
    ├── ps1-memory-strategy.md         # ← docs/ps1-memory-strategy.md
    ├── ps1_asset_pipeline_plan.md     # ← docs/ps1_asset_pipeline_plan.md
    ├── ps1_large_rpg_optimization_reference.md
    ├── ps1godot-ue5-editor-port-plan.md
    ├── ps1graph-ue-blueprint-port-plan.md
    ├── ps1godot_blender_addon_integration_plan.md
    ├── psxsplash-improvements.md      # (if kept here vs reference/)
    ├── splashpack-format.md           # (if kept here vs reference/)
    ├── demo-blueprint.md
    ├── demo-showcase-setup.md
    ├── lua-editor-setup.md
    ├── lua-ps1-cheatsheet.md
    ├── psxsplash-improvements.md
    └── README.md                      # explain "this is internal — not on the site"
```

Notes:
- `mkdocs.yml`'s `nav:` block determines what's on the site. Files in
  `internal/` get omitted there.
- The `(anchor: …)` notes mean: existing content is a starting point
  for the new page, but the new page should be authored fresh with the
  intended audience (an author opening the editor for the first time).
- Several existing files cover similar ground (`docs/handoff-*.md`,
  `docs/projects/`, `docs/rfc/`) — they stay in the repo as `internal/`
  so future-Claude sessions have full context but new users don't have
  to wade through them.

---

## Screenshot capture checklist (USER WORK)

The biggest leverage for the docs site is screenshots. Walk this list
top to bottom; capture each numbered shot at the suggested resolution,
save into `docs/_screens/<section>/` per the table. The MkDocs
Material theme handles light/dark mode automatically so capture in
whichever theme you author in.

**Conventions:**
- All shots at 1920×1080 native (or 1280×720 if your monitor is
  smaller) — the site downscales for thumbnails.
- PNG, not JPG (no compression artifacts on UI text).
- Crop tight when the surrounding editor isn't relevant (e.g., a
  single dock or a single node). Use a tool like ShareX (Windows) or
  Flameshot (Linux) for region capture.
- For PSX runtime captures: PCSX-Redux's built-in screenshot
  (`F12` or whatever you mapped) at the native 320×240 resolution.
  We'll show those at 2x or 3x scaled with nearest-neighbor.

### Tier 1 — landing + getting started (capture first; ship slice 1)

1. **Hero shot** — Godot editor with PS1Godot dock open, monitor.tscn
   loaded in the 3D view, PCSX-Redux running the game in a smaller
   window beside it. Save: `_screens/landing/hero.png`. Used on
   `index.md`.
2. **First launch** — Godot with `[PS1Godot] Plugin enabled.` visible
   in the Output panel. Cropped to Output panel + a corner of the
   editor for context. Save: `_screens/getting-started/plugin-enabled.png`.
3. **Run-on-PSX button** — close-up of the big red `▶ Run on PSX`
   button on the right-side dock. Save: `_screens/getting-started/run-button.png`.
4. **Tutorial sequence** — 3 shots showing the first-scene tutorial
   in order: empty scene → cube + PS1 shader applied → cube boots
   in PCSX-Redux. Save: `_screens/getting-started/first-scene-{1,2,3}.png`.

### Tier 2 — per-dock guide (slice 4 main pass)

5. **Bottom-panel strip** — the new 4-tab layout (`PS1 Graph`,
   `PS1 Doctor`, `PS1 Authoring`, `PS1 Tools`). Crop to just the
   tabs. Save: `_screens/docks/strip.png`.
6. **PS1Godot main dock (right side)** — full dock vertical with all
   sections visible: scene budgets, Run-on-PSX, Quick Actions, Setup
   summary, VRAM thumbnail. May need to expand the dock to its full
   height. Save: `_screens/docks/ps1godot-panel.png`.
7. **PS1 Graph dock** — `test.tres` loaded showing the dialogue
   graph with all node kinds visible (Hello + choice + branches +
   snippet + sub-dialogue). Save: `_screens/docks/graph.png`.
8. **PS1 Graph dock — palette open** — right-click on the canvas
   showing the kind palette with tooltips on hover. Save:
   `_screens/docks/graph-palette.png`.
9. **PS1 Graph dock — close-up of tinted nodes** — three side-by-side
   nodes from the three graph kinds (Dialogue blue, FSM teal, Quest
   amber). Crop to ~600px wide. Save: `_screens/docks/graph-tints.png`.
10. **PS1 Doctor dock** — loaded with the monitor scene's warnings,
    categories expanded. Save: `_screens/docks/doctor.png`.
11. **PS1 Authoring container** — opened, sub-tab strip visible.
    Save: `_screens/docks/authoring.png`.
12. **PS1 Tools container** — same. Save: `_screens/docks/tools.png`.
13. **UI Canvas editor** (sub-tab of Authoring) — `dialogue_box`
    canvas loaded, elements visible. Save: `_screens/docks/ui-canvas.png`.
14. **VRAM Viewer** (sub-tab of Authoring) — post-F5, showing the
    monitor scene's packed atlases + CLUTs + framebuffer reserves.
    Save: `_screens/docks/vram-viewer.png`.
15. **Audio Routing** (sub-tab of Authoring) — clip list with
    SPU/XA/CDDA badges + sample rate + size. Save:
    `_screens/docks/audio-routing.png`.
16. **PS1 Lua API Cheatsheet** (sub-tab of Authoring) — searchable
    list, mid-search (type "Audio" in the filter). Save:
    `_screens/docks/lua-cheatsheet.png`.
17. **PS1 Quest Journal** (sub-tab of Authoring) — `village_quest.tres`
    loaded, mid-progression (find_npc completed, talk_to_npc active).
    Save: `_screens/docks/quest-journal.png`.
18. **PS1 Graph Find** (sub-tab of Tools) — search results for
    `breathing_low` across the graphs. Save: `_screens/docks/graph-find.png`.
19. **PS1 References** (sub-tab of Tools) — asset reference list for
    one of the monitor textures. Save: `_screens/docks/references.png`.
20. **PS1 Lua REPL** (sub-tab of Tools) — REPL input + a couple of
    scrollback lines showing roundtrip. Save: `_screens/docks/repl.png`.

### Tier 3 — graph authoring per kind

21. **Dialogue graph full** — `test.tres` zoomed-to-fit, all nodes
    visible. Save: `_screens/graphs/dialogue-full.png`.
22. **Dialogue node inspector** — a Line node selected, side-pane
    inspector visible showing all payload fields (text, speaker,
    audio, skippable, notifies, reveal mode, reveal rate). Save:
    `_screens/graphs/dialogue-inspector.png`.
23. **FSM graph full** — `bot_brain.tres`. Save:
    `_screens/graphs/fsm-full.png`.
24. **FSM state inspector** — a State node selected, side-pane
    showing on_enter / on_update / on_exit slots. Save:
    `_screens/graphs/fsm-inspector.png`.
25. **Quest graph full** — `village_quest.tres`. Save:
    `_screens/graphs/quest-full.png`.
26. **Quest objective inspector** — an Objective node selected.
    Save: `_screens/graphs/quest-inspector.png`.
27. **Behavior Tree graph full** — small BT (Selector + a couple
    Leaves) you build for the doc. Save: `_screens/graphs/bt-full.png`.

### Tier 4 — runtime / PSX-side captures

28. **Typewriter mid-reveal** — PSX screen capturing the moment
    "Hell" is shown but "o" hasn't appeared yet (mid-typewriter on
    "Hello" at rate=5). Save: `_screens/runtime/typewriter-mid.png`.
29. **Typewriter complete** — same line, fully revealed. Save:
    `_screens/runtime/typewriter-done.png`.
30. **Dialogue with choice** — the choice node on PSX with 3 options
    visible. Save: `_screens/runtime/choice.png`.
31. **Sub-dialogue active** — PSX showing the "Heller" line from the
    sub_dialogue smoke fixture. Save: `_screens/runtime/sub-dialogue.png`.
32. **Run-on-PSX export console** — Godot Output panel mid-export,
    showing the auto-recompile + asset reports + LoaderPack written.
    Save: `_screens/runtime/export-log.png`.
33. **Toast notifier** — the green/amber/red notification panel
    in the bottom-right after F5. Save: `_screens/runtime/toast.png`.
34. **PS1AudioClip waveform thumbnails** — FileSystem dock showing
    several `.tres` audio clips with their auto-generated waveforms
    + route stripes (amber/blue/green). Save:
    `_screens/runtime/audio-thumbs.png`. **Note:** requires deleting
    `.godot/imported/` first to refresh the cache.

### Tier 5 — node / component reference

35. **Inspector panels** for each major custom node. Open the scene
    that has that node, select it, capture the inspector. One per:
    PS1Scene, PS1Camera, PS1Player, PS1MeshInstance, PS1SkinnedMesh,
    PS1Animation, PS1Cutscene, PS1AudioClip, PS1TriggerBox,
    PS1UICanvas, PS1Room, PS1PortalLink, PS1Sky. Save:
    `_screens/nodes/<node-name>-inspector.png`.

That's ~50 screenshots total. Tier 1+2 (~20 shots) is enough to
ship the docs site with a Getting Started + Docks section. Tier 3-5
can come in over a few iterations.

---

## Restructure execution order (next session)

Recommended sequence:

1. **Verify CI Run #6** is green (or fix and re-run). If green,
   download the per-OS GDExtension artifacts.
2. **v0.5.1 release with cross-platform binaries** — bundle the
   downloaded `.so` / `.framework`, re-run build-release.py, tag,
   publish.
3. **Scaffold mkdocs site** — add `mkdocs.yml`, theme config,
   GitHub Pages deploy workflow. Empty content; just verify the
   deploy pipeline.
4. **Restructure existing `docs/`** per the mapping above. Aim:
   `mkdocs serve` shows a navigable site with all existing content
   reachable. Some pages will still be heavy/internal-flavored
   — fix that in slice 4.
5. **Lua API auto-generator** — `scripts/py/gen_lua_api_docs.py`
   reads `addons/ps1godot/scripting/api/lua_api.lua`, emits one .md
   per namespace.
6. **Worked examples mining** — extend the generator to grep .lua
   files for usages and inline the snippets.
7. **Tier 1 screenshots + Getting Started rewrite** — the highest-
   leverage user-facing doc. Use the captured shots.
8. **Tier 2 screenshots + Docks pages** — second-highest leverage,
   mostly mechanical write-up once shots are in.
9. **Iterate** — Tier 3+ as bandwidth allows. Per-node guide is the
   longest tail.

---

## What lives in memory

Added user-level memory entry for the Godot 4 .NET shutdown-crash
pattern (GC.Collect + WaitForPendingFinalizers at end of _ExitTree
drains the finalizer queue while native side is still alive). That
applies to any Godot 4 .NET plugin project, not just PS1Godot, so it
belongs in user-level memory rather than the PS1Godot repo.

PS1Godot-specific context lives in `CLAUDE.md` (architecture) and
this handoff doc (current state + plan). No PS1Godot-specific memory
entries created — the project moves fast enough that the .md files in
the repo are a better source of truth.

---

## Open follow-ups not in the docs work

These are filed for awareness; pick up whenever:

- **macOS framework path in `.gitignore`** — once CI starts producing
  `.framework` directories, verify the gitignore handles them (it
  may need `*.framework/` added).
- **godot-cpp local divergence** — the working-tree checkout has a
  one-commit private tweak (`d328849`) on top of upstream
  `4862a9d`. Decide: (a) drop the local tweak and re-sync to
  upstream (simplest), (b) propose the tweak upstream as a PR, or
  (c) carry it as a tracked patch file. Currently invisible because
  the local checkout works and CI uses upstream.
- **Test the actual Linux build of the plugin** on a real Linux
  machine (or VM). CI proves it compiles; doesn't prove the
  editor-side workflow actually feels right.
