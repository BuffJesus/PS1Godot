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

## Tier ordering (what to shoot first)

The prior handoff ranks ~50 shots into five tiers; the priority
ordering for *capture* is:

1. **Tier 1** — landing + getting started (4 shots). Highest
   per-shot value. The hero shot lands on `index.md` and sets the
   visual tone for everyone landing fresh.
2. **Tier 2** — per-dock (16 shots). Unblocks the per-dock guide
   pages in `docs/docks/` that aren't written yet.
3. **Tier 3** — graph authoring (7 shots). Pairs with the existing
   `docs/authoring/graphs/*.md` pages.
4. **Tier 4** — runtime / PSX-side (7 shots). Needs PCSX-Redux
   running the demo or a smoke fixture.
5. **Tier 5** — per-node inspector (13 shots). Mostly mechanical
   once the rest of the pipeline is in place.

Tier 1+2 (~20 shots) is enough to ship visible improvement on the
site. Tier 3–5 can come in over a few iterations.

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
