# Editing Lua scripts — setup

Godot's built-in script editor highlights `.lua` files out of the box —
the `ps1lua.gdextension` that ships with PS1Godot registers both the
language and a `PS1 Lua` syntax highlighter. For day-to-day editing
inside Godot, there's no setup.

For heavier work (multi-file navigation, autocomplete for the
PS1Godot runtime API) route to an external editor:

1. **In-Godot highlighting** — works automatically. If a `.lua` file
   opens as plain text (the tab is stuck on a prior "Plain Text" choice),
   pick `PS1 Lua` from the Syntax Highlighter dropdown in the script
   editor toolbar. Godot remembers per tab.
2. **External editor for autocomplete** — Rider / VS Code / Zed, using
   the auto-generated EmmyLua stubs for `Entity.*`, `Camera.*`, etc.

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

- **In-Godot autocomplete**: Godot's built-in code-completion only
  works for languages with a real semantic analyzer — our
  ScriptLanguageExtension is a visual-only stub. Use the external
  editor path for completion.
- **Runtime execution in editor**: Lua runs on PSX, not in Godot. The
  cheatsheet explains which forms the rewriter skips.

## First-time Godot highlighter setup per file

Godot caches per-tab highlighter choices. If you edited a `.lua` file
in Godot before `ps1lua.gdextension` shipped (pre-2026-04-23), its
tab state remembers "Plain Text" and won't auto-switch to `PS1 Lua`.
One-time fix per file:

1. Open the `.lua` in the Godot script editor
2. In the top-right toolbar (or the editor's `⋮` menu) find the
   **Syntax Highlighter** dropdown
3. Pick **PS1 Lua**

Godot persists the choice. Only needed once per previously-opened file
— fresh files auto-match.
