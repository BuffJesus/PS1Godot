# Adding screenshots

The site has zero screenshots today and the hand-written authoring
guides (per-node, per-dock) are blocked on that pass. This page is
the playbook for capturing and adding them — one read-path for "I
just took a screenshot, where does it go and how do I reference it?"

## Where they live

```
docs/
└── _screens/                          ← all images, sectioned by topic
    ├── landing/                       ← hero shot on index.md
    ├── getting-started/               ← install / quickstart / first-scene
    ├── docks/                         ← every editor dock
    ├── graphs/                        ← per-graph-kind full + inspector
    ├── runtime/                       ← PSX-side captures (PCSX-Redux)
    └── nodes/                         ← per-node inspector shots
```

The leading underscore is a hint to readers that the directory is
asset storage, not a documentation tree. MkDocs treats it as static
content — files copy through to the built site unchanged and are
reachable via relative URLs from any page.

Create the per-section subdirectory the first time you drop a shot
into it (`docs/_screens/landing/` etc.) — they're not pre-created
to keep the tree tidy.

## File format and resolution

| Surface | Format | Native resolution | Notes |
|---|---|---|---|
| Godot editor | PNG | 1920×1080 or 1280×720 | Lossless. Site downscales for thumbnails. |
| Per-dock / per-node closeups | PNG | Crop tight | Drop surrounding editor chrome when not relevant. |
| PSX runtime captures | PNG | 320×240 native | Use PCSX-Redux's built-in screenshot (`F12`). Site displays at 2× / 3× scaled with nearest-neighbor. |

**Always PNG**, never JPG — JPEG compression artifacts mangle pixel
UI text and 320×240 PSX captures. The few extra KB don't matter at
scroll-past sizes.

## Capture tools

| OS | Tool | Notes |
|---|---|---|
| Windows | [ShareX](https://getsharex.com/) | Region capture, autonames, can pipe straight into a watched folder. |
| Linux | [Flameshot](https://flameshot.org/) | `flameshot gui` for region select. |
| macOS | Shift-Cmd-4 | Region capture, lands on Desktop. |
| PSX runtime | PCSX-Redux F12 | Outputs to its configured screenshots dir. |

For animated UI (typewriter reveal, toast fade, etc.) capture
sequential PNGs and pick the most-readable single frame — the site
doesn't ship animated content for v1.

## Naming

Use lowercase, hyphen-separated, descriptive:

```
docs/_screens/landing/hero.png
docs/_screens/getting-started/plugin-enabled.png
docs/_screens/getting-started/run-button.png
docs/_screens/getting-started/first-scene-1.png
docs/_screens/getting-started/first-scene-2.png
docs/_screens/docks/strip.png
docs/_screens/docks/ps1godot-panel.png
docs/_screens/docks/graph.png
docs/_screens/docks/graph-palette.png
docs/_screens/docks/graph-tints.png
docs/_screens/nodes/ps1-scene-inspector.png
docs/_screens/nodes/ps1-mesh-instance-inspector.png
docs/_screens/runtime/typewriter-mid.png
docs/_screens/runtime/typewriter-done.png
```

Sequential shots get `-1`, `-2`, `-3` suffixes. Inspector shots get
`<node-slug>-inspector.png`. The prior internal handoff
([`docs/internal/handoff-2026-05-18-docs-site-plan.md`](https://github.com/BuffJesus/PS1Godot/blob/main/docs/internal/handoff-2026-05-18-docs-site-plan.md){ target="_blank" })
has the full ~50-shot Tier 1–5 checklist with exact filenames for
every planned shot.

## Embedding in pages

Standard Markdown image syntax, relative path from the doc:

```markdown
![PS1Godot dock with the demo monitor scene loaded](../_screens/docks/ps1godot-panel.png)
```

The depth prefix (`../_screens/...`) varies with the doc's location:

| Doc location | Prefix |
|---|---|
| `docs/index.md` | `_screens/...` |
| `docs/getting-started/*.md` | `../_screens/...` |
| `docs/authoring/*.md` | `../_screens/...` |
| `docs/authoring/graphs/*.md` | `../../_screens/...` |
| `docs/authoring/audio/*.md` | `../../_screens/...` |
| `docs/docks/*.md` (planned) | `../_screens/...` |
| `docs/contributing/*.md` | `../_screens/...` |

`mkdocs build --strict` fails on a broken image reference, so the
CI gate catches typos and depth-prefix mistakes immediately.

### Captions

For shots that need explanation, use a caption right under the image:

```markdown
![Bottom-panel strip with the new 4-tab layout](../_screens/docks/strip.png)
*The four consolidated bottom-panel tabs landed in v0.4.0,
replacing the previous 10-tab strip.*
```

The italic line is rendered as a paragraph below the image — clean
and theme-consistent without needing a custom shortcode.

### Click-to-zoom (optional)

For dense screenshots where the detail matters (the VRAM viewer,
the Doctor warnings list), wrap the image in a link to itself for
a native "open full size" behavior:

```markdown
[![VRAM viewer, monitor scene atlases](../_screens/docks/vram-viewer.png)](../_screens/docks/vram-viewer.png)
```

A formal lightbox plugin
([`mkdocs-glightbox`](https://github.com/blueswen/mkdocs-glightbox))
is available if we add many of these; for now the self-link is
adequate.

## Capture punch list

Tier 1+2 (~22 shots) lands visible site improvement; Tier 3–5 fills
in the long tail. Filenames are exact — `--strict` will fail any
embed that mistypes them.

### Tier 1 — landing + getting started

The hero + getting-started shots have the highest per-shot value;
they set the visual tone and unblock `index.md` + the first-scene
tutorial.

- [ ] **`landing/hero.png`** — Godot editor with PS1Godot dock open,
      `demo.tscn` (or a smoke scene) loaded in the 3D view,
      PCSX-Redux running the same scene in a smaller window beside
      it. Lands on `index.md`.
- [ ] **`getting-started/plugin-enabled.png`** — Godot with
      `[PS1Godot] Plugin enabled.` visible in the Output panel.
      Cropped to Output + a corner of the editor.
- [ ] **`getting-started/run-button.png`** — close-up of the big
      red **▶ Run on PSX** button on the right-side dock.
- [ ] **`getting-started/first-scene-1.png`** — first-scene
      tutorial mid-author: empty Node3D, just promoted to `PS1Scene`.
- [ ] **`getting-started/first-scene-2.png`** — same scene with
      floor + textured cube + PS1 shader applied.
- [ ] **`getting-started/first-scene-3.png`** — the finished cube
      booting in PCSX-Redux.

### Tier 2 — per-dock guide

Each shot anchors a future page under `docs/docks/`. The dock-page
pass is gated on these.

- [ ] **`docks/strip.png`** — the 4-tab bottom-panel strip
      (PS1 Graph · PS1 Doctor · PS1 Authoring · PS1 Tools). Crop
      tight to just the tabs.
- [ ] **`docks/ps1godot-panel.png`** — full right-side dock
      vertical: scene budgets, Run-on-PSX, Quick Actions, Setup
      summary, VRAM thumbnail. Expand to full editor height.
- [ ] **`docks/graph.png`** — `test.tres` loaded showing the
      dialogue graph with all node kinds visible (Hello + choice +
      branches + snippet + sub-dialogue).
- [ ] **`docks/graph-palette.png`** — right-click on the canvas
      showing the kind palette with tooltips on hover.
- [ ] **`docks/graph-tints.png`** — three side-by-side nodes from
      different graph kinds (Dialogue blue, FSM teal, Quest amber).
      Crop to ~600px wide.
- [ ] **`docks/doctor.png`** — Doctor dock loaded with the demo
      scene's warnings, categories expanded.
- [ ] **`docks/authoring.png`** — PS1 Authoring container opened,
      sub-tab strip visible.
- [ ] **`docks/tools.png`** — PS1 Tools container, same shape.
- [ ] **`docks/ui-canvas.png`** — UI Canvas editor sub-tab,
      `dialogue_box` canvas loaded, elements visible.
- [ ] **`docks/vram-viewer.png`** — VRAM Viewer post-F5, showing
      the demo scene's packed atlases + CLUTs + framebuffer reserves.
- [ ] **`docks/audio-routing.png`** — Audio Routing clip list with
      SPU / XA / CDDA badges + sample rate + size.
- [ ] **`docks/lua-cheatsheet.png`** — PS1 Lua API Cheatsheet
      mid-search (type `Audio` in the filter).
- [ ] **`docks/quest-journal.png`** — `village_quest.tres` loaded,
      mid-progression (`find_npc` completed, `talk_to_npc` active).
- [ ] **`docks/graph-find.png`** — Graph Find search results for
      `breathing_low` across the graphs.
- [ ] **`docks/references.png`** — asset reference list for one of
      the demo textures.
- [ ] **`docks/repl.png`** — Lua REPL input + a couple of scrollback
      lines showing roundtrip.

### Tier 3 — graph authoring per kind

Pairs with the existing pages under `docs/authoring/graphs/`.

- [ ] **`graphs/dialogue-full.png`** — `test.tres` zoomed-to-fit,
      all nodes visible.
- [ ] **`graphs/dialogue-inspector.png`** — a Line node selected,
      side-pane inspector showing payload fields (text, speaker,
      audio, skippable, notifies, reveal mode, reveal rate).
- [ ] **`graphs/fsm-full.png`** — `bot_brain.tres`.
- [ ] **`graphs/fsm-inspector.png`** — a State node selected,
      side-pane showing on_enter / on_update / on_exit slots.
- [ ] **`graphs/quest-full.png`** — `village_quest.tres`.
- [ ] **`graphs/quest-inspector.png`** — an Objective node selected.
- [ ] **`graphs/bt-full.png`** — small BT (Selector + a couple of
      Leaves) you build for the doc.

### Tier 4 — runtime / PSX-side captures

PCSX-Redux's `F12` at native 320×240. The site upscales 2×/3× with
nearest-neighbor.

- [ ] **`runtime/typewriter-mid.png`** — mid-reveal: "Hell" shown,
      "o" hasn't appeared yet ("Hello" at rate=5).
- [ ] **`runtime/typewriter-done.png`** — same line, fully revealed.
- [ ] **`runtime/choice.png`** — the choice node on PSX with 3
      options visible.
- [ ] **`runtime/sub-dialogue.png`** — PSX showing the "Heller"
      line from the `sub_dialogue` smoke fixture.
- [ ] **`runtime/export-log.png`** — Godot Output panel mid-export
      showing the auto-recompile + asset reports + LoaderPack
      written.
- [ ] **`runtime/toast.png`** — green/amber/red notification panel
      in the bottom-right after F5.
- [ ] **`runtime/audio-thumbs.png`** — FileSystem dock showing
      several `.tres` audio clips with their auto-generated waveforms
      + route stripes (amber/blue/green). Requires deleting
      `.godot/imported/` first to refresh the cache.

### Tier 5 — per-node inspector

Mechanical — open the demo scene, select the named node, capture
the inspector. One per node. Each filename is
`nodes/<node-slug>-inspector.png`.

- [ ] `nodes/ps1-scene-inspector.png`
- [ ] `nodes/ps1-mesh-instance-inspector.png`
- [ ] `nodes/ps1-skinned-mesh-inspector.png`
- [ ] `nodes/ps1-camera-inspector.png`
- [ ] `nodes/ps1-player-inspector.png`
- [ ] `nodes/ps1-animation-inspector.png`
- [ ] `nodes/ps1-cutscene-inspector.png`
- [ ] `nodes/ps1-audio-clip-inspector.png`
- [ ] `nodes/ps1-trigger-box-inspector.png`
- [ ] `nodes/ps1-ui-canvas-inspector.png`
- [ ] `nodes/ps1-room-inspector.png`
- [ ] `nodes/ps1-portal-link-inspector.png`
- [ ] `nodes/ps1-sky-inspector.png`

## When you're done with a batch

1. Drop the PNGs into the right `docs/_screens/<section>/` dir.
2. For each shot, find the page that should reference it and add
   the `![...](../_screens/...)` line in the right section.
3. `python -m mkdocs build --strict` locally — confirms no broken
   image paths or stale references.
4. Commit + push. The docs CI re-runs `--strict` and deploys the
   updated site.

If a shot will reference a page that doesn't exist yet (per-node /
per-dock pages from the restructure mapping), create the new page
with the screenshot + a 3–5 sentence intro at minimum. An empty
stub adds nothing; a one-paragraph "this is the dock and here's
what it does + a screenshot" is real value.
