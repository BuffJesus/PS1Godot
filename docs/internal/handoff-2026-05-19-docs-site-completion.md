# Handoff — docs site live, mechanical slices closed (2026-05-19)

This session executed the docs-site plan from the prior handoff
(`handoff-2026-05-18-docs-site-plan.md`). All mechanical slices are
shipped and **the site is live at <https://buffjesus.github.io/PS1Godot/>**.
Remaining work needs either content judgment (merging the two
tutorials, writing per-node / per-dock guides), user screenshots
(Tiers 1–5 in the prior handoff), or hardware (Linux smoke, godot-cpp
tweak decision).

**HEAD `f858baa`**, in sync with `origin/main`. Docs workflow #9 green.

## What shipped this session (10 commits)

```
f858baa docs(site): refresh landing page note + add Lua API to bullet list
4bc693d docs(site): pull SETUP / QUICKSTART into docs/getting-started
0097574 ci(docs): fail when lua-api pages drift from luaapi.hh
e03f61e docs(site): clear tutorial link warnings, flip CI to --strict
27825a5 docs(lua-api): mine worked examples from demo scripts
d96ee42 docs(lua-api): generate per-namespace pages from luaapi.hh
89b8268 docs(site): restructure docs/ into authoring/reference/internal
79a2f33 docs(site): scaffold MkDocs site + Pages deploy
aa14c62 fix(scripting): macos library paths .framework -> .universal.dylib
   (… plus the v0.5.1 release-asset replacement, no commit — asset
   swap on the existing release: Windows DLLs in the plugin zip
   refreshed from bloated local builds to CI builds, 7.3 MB → 5.3 MB.)
```

## Site state

The published nav:

- **Home** — landing with "Where to start" funnel.
- **Getting started** — Install, Quickstart, hello-cube tutorial,
  basic-scene tutorial.
- **Authoring** — fixed cameras, four graph pages (dialogue, FSM,
  quest, behavior tree), audio routing + sequenced music, UI canvas
  + custom boot logo + SplashEdit import.
- **Lua API** — overview + 24 namespace pages (145 entries, 30 with
  worked examples mined from `godot-ps1/demo/scripts/`).
- **Reference** — splashpack format, API showcase, Lua cheatsheet,
  Lua editor setup, psxsplash improvements.

`docs/internal/` is excluded from the build via `exclude_docs` —
handoffs, RFCs, archive, planning docs live there for project authors
and future sessions, browsable on GitHub only.

## What's wired into CI

`mkdocs build --strict` runs on every push to `main` (or `workflow_dispatch`),
gated on changes to `docs/**`, `mkdocs.yml`, `requirements-docs.txt`,
`.github/workflows/docs.yml`, `scripts/py/gen_lua_api_docs.py`, or
`psxsplash-main/src/luaapi.hh`. Build steps:

1. Regenerate `docs/lua-api/*.md` from `psxsplash-main/src/luaapi.hh`.
2. `git diff --exit-code docs/lua-api/` — fails if the committed
   pages drift from the regenerated set. The live site rebuilds from
   source either way, but the committed files serve GitHub-side
   readers + crawlers and should stay in sync.
3. `mkdocs build --strict`.
4. Deploy via `actions/deploy-pages@v4`.

Pages was enabled via API (`POST /repos/.../pages` with
`build_type=workflow`). First push run failed because that toggle
wasn't set yet; subsequent runs are green.

## Tools added

- **`scripts/py/gen_lua_api_docs.py`** — parses `// Namespace.Method(args)
  -> ret` signature comments from `psxsplash-main/src/luaapi.hh`,
  groups by namespace, emits one `.md` per namespace plus an
  `index.md` overview. Generated pages carry an HTML-comment sentinel
  so the wipe-and-write pass only deletes auto-generated files
  (handwritten siblings like a future `recipes.md` survive). Aborts
  non-zero on 0-entry parse to surface luaapi.hh format changes.
- **Worked-example mining** — same script scans git-tracked
  `.lua` under `godot-ps1/demo/scripts/`, `godot-ps1/lua/`, and
  `godot-ps1/addons/ps1godot/templates/scripts/`. Per (namespace,
  method), picks the cleanest invocation (source-subdir rank +
  call-line length + leading-comment bonus) and inlines it as a
  fenced lua snippet with a file:line reference. Git-tracked filter
  prevents gitignored content (e.g. `godot-ps1/lua/monitor/` jam
  scripts) from leaking as references that 404 for everyone else.
- **DRY debt:** `gen_lua_api_docs.py`'s SIG_LINE regex + parse() are
  duplicated from `godot-ps1/addons/ps1godot/scripting/gen_api_data.py`
  (which feeds the in-editor autocomplete). Next time either side
  needs a fix, factor into `tools/lua_api_parser.py` or similar.

## Open follow-ups (judgment / hardware / user-side)

1. **Merge `tutorial-hello-cube.md` + `tutorial-basic-scene.md` into
   `docs/getting-started/first-scene.md`** per the prior handoff's
   restructure mapping. Content judgment — the two existing tutorials
   target slightly different audiences (look-and-feel vs interactive
   slice). Worth a fresh draft rather than mechanical concat.
2. **Per-node guides under `docs/authoring/nodes/`** — 15 stubs
   pending (PS1Scene, PS1MeshInstance, PS1SkinnedMesh, PS1Camera,
   PS1Player, PS1Animation, PS1Cutscene, PS1AudioClip, PS1TriggerBox,
   PS1UICanvas, PS1Room, PS1PortalLink, PS1Sky, plus the music nodes).
   Best done alongside the screenshot pass — node inspector shots
   from Tier 5 in the prior handoff are the visual anchor.
3. **Per-dock guides under `docs/docks/`** — 14 docks (Tier 2 of the
   prior handoff's screenshot list). Same as above — pair the page
   with the screenshot.
4. **Contributing section** — `docs/contributing/{architecture,
   building, adding-a-node-kind, adding-a-graph-kind, ci}.md`.
   Architectural overview is the only one with real prerequisite
   research; the rest are recipes derived from the build-gdextension
   workflow + existing CLAUDE.md.
5. **Screenshots Tier 1–5** — purely user work. The prior handoff's
   §Screenshot capture checklist is the playbook. Hero shot + the
   bottom-panel strip + the right-side dock are the three highest-
   leverage shots; everything else falls out naturally once those
   land.
6. **Linux smoke** of the built plugin on a real Linux box (CI proves
   compile, not workflow). godot-cpp local tweak decision still open
   per the prior handoff.

## How to extend the docs site

- **Adding a new namespace to `luaapi.hh`:** add the signature
  comments in the standard `// Namespace.Method(args) -> ret` shape,
  run `python scripts/py/gen_lua_api_docs.py`, add the new page to
  the `nav:` block under "Lua API" in `mkdocs.yml`. CI's diff-check
  catches missed regens; nav drift is still manual (24 entries is
  small enough that hand-listing is fine — flag if it churns).
- **Adding a hand-written page in any section:** write the .md,
  add to `mkdocs.yml` nav. `--strict` will fail CI if links break.
  Internal cross-doc refs use the post-restructure paths
  (`docs/internal/`-excluded targets need to be external GitHub URLs
  if you want them to render).
- **Adding to `docs/internal/`:** anything in that subtree is
  auto-excluded from the build via `exclude_docs`. Cross-link to
  these from rendered pages with external GitHub URLs.

## Suggested opener for next session

> "HEAD `f858baa`, in sync with `origin/main`. PS1Godot docs site
> live at <https://buffjesus.github.io/PS1Godot/> — Getting started,
> Authoring, Lua API (24 namespaces auto-generated, 30 entries with
> worked examples), and Reference all in the nav. CI strict + drift
> check active. Remaining work is content judgment (merge tutorials),
> screenshots (user-side, Tier 1–5), or hardware (Linux smoke). Pick
> what next? Per-node stub round, contributing/architecture write-up,
> or stop here and let the user drive screenshots?"
