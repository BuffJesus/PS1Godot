# Continuous integration

PS1Godot's CI runs two unrelated workflows. Knowing which one to look
at when something fails saves a lot of log-spelunking.

## `build-gdextension.yml` — PS1Lua native binaries

[`.github/workflows/build-gdextension.yml`](https://github.com/BuffJesus/PS1Godot/blob/main/.github/workflows/build-gdextension.yml){ target="_blank" }

Builds the PS1Lua GDExtension across three OSes × three targets and
uploads one artifact per (OS, target) pair. The plugin zip in each
GitHub Release bundles all nine binaries so Linux and macOS users
don't need SCons locally to get syntax highlighting.

| OS | Targets | Output |
|---|---|---|
| `windows-latest` | editor, template_debug, template_release | `libps1lua.windows.*.x86_64.dll` |
| `ubuntu-latest` | editor, template_debug, template_release | `libps1lua.linux.*.x86_64.so` |
| `macos-latest` | editor, template_debug, template_release | `libps1lua.macos.*.universal.dylib` |

**Trigger paths:**
`godot-ps1/addons/ps1godot/scripting/**`,
`.github/workflows/build-gdextension.yml`, and any `v*` tag.

**Per-job sequence:**

1. Checkout (with submodules).
2. Install SCons via pip.
3. Clone `godot-cpp` pinned to a specific SHA (currently
   `4862a9dcf1471c9ea19680b9faadb5b6a9432092`). The pin matters —
   floating to `godot-4.4-stable` HEAD has burned a CI run when
   upstream made a breaking change.
4. Restore the `godot-cpp` build cache (~10 min savings — building
   the bindings from scratch dominates wall time).
5. `scons platform=<windows|linux|macos> target=<editor|template_debug|template_release> -j2`.
6. Upload `build/*.dll|*.so|*.dylib|*.framework` as an artifact
   named `ps1lua-<platform>-<target>`.

A parallel `plugin-csharp` job runs `dotnet build` on each OS as a
lightweight regression check that the C# side compiles cleanly
without loading the editor. There's a wrinkle in that job: the
project pins `Godot.NET.Sdk/4.7.0-dev.5`, a pre-release SDK that
isn't on nuget.org. CI downloads the matching Godot build, locates
the `GodotSharp/Tools/nupkgs/` directory inside it, and points
`GODOT_NUPKGS` there so the SDK package resolves. Bump
`GODOT_VERSION` in the workflow alongside the `.csproj` SDK pin.

### Bumping the `godot-cpp` SHA

1. Grab a new known-good SHA:
   ```bash
   curl -s https://api.github.com/repos/godotengine/godot-cpp/commits/master | jq -r .sha
   ```
2. Update `GODOT_CPP_SHA` in `build-gdextension.yml` (both the env
   var and the cache key — the key embeds the SHA so a bump
   invalidates the cache cleanly).
3. Push; verify the matrix is still green.

If devs are also building locally, match the local
`godot-ps1/addons/ps1godot/scripting/lib/godot-cpp` HEAD to the new
SHA so dev + CI builds stay aligned.

### Releasing with these artifacts

`scripts/py/build-release.py` reads the binaries out of
`godot-ps1/addons/ps1godot/scripting/build/` and zips them into the
plugin asset attached to a release. So the release flow is:

1. CI runs green on the tag commit → nine artifacts uploaded.
2. Download each artifact zip; extract the single binary into
   `godot-ps1/addons/ps1godot/scripting/build/`.
3. Run `python scripts/py/build-release.py <version>` to produce
   `dist/PS1Godot-plugin-<version>.zip` containing the plugin tree
   with all nine binaries pre-bundled under `build/`.
4. Attach to the GitHub Release.

The internal `2026-05-18` and `2026-05-19` handoffs under
[`docs/internal/`](https://github.com/BuffJesus/PS1Godot/tree/main/docs/internal){ target="_blank" }
walk through the v0.5.x release sequence end-to-end if you need a
reference.

## `docs.yml` — MkDocs site

[`.github/workflows/docs.yml`](https://github.com/BuffJesus/PS1Godot/blob/main/.github/workflows/docs.yml){ target="_blank" }

Builds and deploys this site to GitHub Pages on every push to `main`.

**Trigger paths:**
`docs/**`, `mkdocs.yml`, `requirements-docs.txt`,
`.github/workflows/docs.yml`,
`scripts/py/gen_lua_api_docs.py`, `psxsplash-main/src/luaapi.hh`.

**Sequence:**

1. Checkout.
2. Set up Python 3.12; restore the pip cache keyed on
   `requirements-docs.txt`.
3. `pip install -r requirements-docs.txt` (currently just
   `mkdocs-material`).
4. Run `scripts/py/gen_lua_api_docs.py` to regenerate
   `docs/lua-api/*.md` from `psxsplash-main/src/luaapi.hh`.
5. **Drift gate:** `git diff --exit-code docs/lua-api/`. If the
   regen produced any changes, the committed pages were stale
   relative to `luaapi.hh` and the workflow fails with a clear
   `::error` pointing at the generator script. The live site
   rebuilds from source either way, but the committed `.md` files
   also serve GitHub-side readers and search crawlers — keeping
   the two in sync prevents stale content shipping silently.
6. `mkdocs build --strict`. Strict mode treats warnings as errors,
   so a broken cross-doc link fails CI loudly rather than landing
   a 404 on the live site.
7. Upload the rendered site as a Pages artifact, then a separate
   `deploy` job calls `actions/deploy-pages@v4`.

### One-time setup

GitHub Pages must be enabled with "Source: GitHub Actions" in
**Settings → Pages**. The workflow header comment notes this; if a
fresh fork hits HTTP 404 in the deploy step, that's the cause. The
toggle can also be flipped via the API:

```bash
curl -X POST \
  -H "Authorization: Bearer $GH_TOKEN" \
  -H "Accept: application/vnd.github+json" \
  -d '{"build_type":"workflow"}' \
  https://api.github.com/repos/<owner>/<repo>/pages
```

### Adding a new Lua API namespace

The source of truth is `psxsplash-main/src/luaapi.hh` — its
structured `// Namespace.Method(args) -> ret` comments above each
binding are parsed by three different tools (`gen_api_data.py` for
in-editor autocomplete, `LuaApiStubGenerator.cs` for EmmyLua, and
`gen_lua_api_docs.py` for this site). Add the signature and any
trailing doc lines in the same shape, then:

1. `python scripts/py/gen_lua_api_docs.py` (re-runs idempotently).
2. Add the new page to the `nav:` block under "Lua API" in
   `mkdocs.yml`. The 24 entries are hand-listed because the surface
   is stable; if it starts churning, consider auto-generating a
   nav fragment instead.
3. Commit both the regenerated `docs/lua-api/*.md` and the
   `mkdocs.yml` change in the same commit so the drift gate stays
   green.

### Adding a non-generated page

1. Write the `.md` under the relevant bucket (`docs/getting-started/`,
   `docs/authoring/`, `docs/reference/`, `docs/contributing/`).
2. Add it to `mkdocs.yml`'s `nav:` block.
3. `mkdocs build --strict` locally to catch broken links before
   CI does.
4. Internal cross-doc refs use post-restructure paths
   (`../authoring/audio/routing.md`, etc.). For links to anything
   under `docs/internal/` — which is `exclude_docs`-ed from the
   build — use an external GitHub URL with `{ target="_blank" }`.
