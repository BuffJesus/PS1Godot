# Editing Lua scripts — setup

Godot's built-in editor doesn't know about Lua — it opens `.lua` files as
plain text with no highlighting or completion. Two complementary paths
fix that:

1. **Route `.lua` to an external editor** (Rider, VS Code, Zed, Sublime).
   Double-click a Lua script in Godot's FileSystem dock and it opens in
   your editor of choice with full syntax highlighting.
2. **Auto-generated EmmyLua stubs** give the external editor real
   autocomplete for the PS1Godot runtime API (`Entity.*`, `Camera.*`,
   `Vec3.*`, etc.).

## Step 1 — point Godot at your editor

Godot has two separate settings that govern external editing: one for
`.cs` files (already wired up for C# editors) and one for everything
else. We want the latter.

### Rider (JetBrains)

**Editor Settings → Text Editor → External** (not the C# section):

- **Use External Editor**: ✅ on
- **Exec Path**: path to `rider64.exe` (usually
  `C:\Program Files\JetBrains\JetBrains Rider 2024.X\bin\rider64.exe`)
- **Exec Flags**: `--line {line} {file}`

Rider opens the file at the correct line. The `PS1Godot.sln` solution
doesn't need to be loaded — Rider opens the single file in a lightweight
view.

### VS Code

Same dialog, different values:

- **Exec Path**: path to `Code.exe` (or the portable `VSCode.exe`)
- **Exec Flags**: `--goto {file}:{line}`

Install the [EmmyLua extension](https://marketplace.visualstudio.com/items?itemName=tangzx.emmylua)
for completion.

### Zed / Sublime / other

The shape is always `<editor> <flags-with-{file}-and-{line}>`. Godot
substitutes `{file}` with the absolute path and `{line}` with the line
number (1-based).

## Step 2 — regenerate the API stubs

The PS1Godot plugin exposes a generator that parses `luaapi.hh` and
emits EmmyLua annotations. External editors with an EmmyLua plugin
pick these up automatically for completion.

**Project → Tools → PS1Godot: Regenerate Lua API stubs**

Output lands at `godot-ps1/demo/scripts/_ps1api.lua`. The generator is
idempotent; re-run whenever `luaapi.hh` gains new bindings (e.g., after
a psxsplash runtime update).

Commit `_ps1api.lua` so team members get the same completions without
each running the generator.

## What you get

In Rider / VS Code with the stubs loaded:

- Hover on `Camera.Shake` → the original `// Camera.Shake(...)` doc
  comment from `luaapi.hh`.
- Typing `Entity.` triggers completion for `Spawn`, `Destroy`, `Find`,
  `GetTag`, … — ~108 bindings.
- `---@param` / `---@return` annotations surface as signature hints.
- Decimal literals you write (`Camera.Shake(0.06, 6)`) still compile
  on PSX — the exporter rewrites them to `FixedPoint.newFromRaw(246)`
  at export time (see `docs/lua-ps1-cheatsheet.md`).

## What you DON'T get

- **In-Godot highlighting**: blocked on the `lua-gdextension` install
  (see below).
- **In-Godot autocomplete**: same.
- **Runtime execution in editor**: Lua runs on PSX, not in Godot. The
  cheatsheet explains which forms the rewriter skips.

## Future: native Godot editing (Phase 3 backlog)

`lua-gdextension-main/` sits in the workspace as a vendored reference.
Installing it into `godot-ps1/addons/lua-gdextension/` would give Godot
full Lua support — highlighting, autocomplete, even execution. Blockers:

- No prebuilt binary in the vendored tree; needs CMake + Lua sources
  to produce `build/libluagdextension.windows.*.dll`.
- Its ScriptLanguageExtension defines its own Lua runtime, which
  diverges from psxlua's NOPARSER build — authors could accidentally
  write Lua that runs in-editor but fails at export.

When we cross that bridge, the plugin should wrap lua-gdextension's
execution to match psxlua semantics (reject decimals pre-rewrite,
document the delta). Tracked as Phase 3 polish.
