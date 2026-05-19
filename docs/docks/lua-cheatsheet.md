# PS1 Lua API Cheatsheet

Under **PS1 Authoring → Lua API**. Searchable in-editor reference
for the PS1Lua runtime API, rendered from the same source the
EmmyLua stub generator uses.

<!-- SCREENSHOT: docks/lua-cheatsheet.png — mid-search, "Audio" typed in filter -->

## Why it exists in-editor

You're already in Godot writing a Lua script — context-switching
to a browser tab to look up `Audio.PlaySfx`'s parameter order is
friction. The cheatsheet sits next to the script editor, takes
text-search, and shows the same docstring your external editor's
autocomplete pulls up.

The data path: `psxsplash-main/src/luaapi.hh`'s structured
signature comments → `gen_api_data.py` → embedded array in the
GDExtension → rendered in this dock. Single source of truth; the
in-editor surface can't drift from the runtime's real API.

## Layout

- **Search box** at the top — filters the tree as you type.
  Matches against method names + namespace names + docstring
  text. Case-insensitive.
- **Reload** button — re-runs the parser if you've modified
  `luaapi.hh` and want the dock to pick up the change without a
  full plugin rebuild.
- **Namespace tree** — `Audio`, `Camera`, `Entity`, `Input`,
  `Music`, `Scene`, `Sound`, `UI`, … expandable, methods listed
  per namespace.
- **Detail pane** (right) — selected method's full signature,
  docstring, and a **Copy signature** button for paste-into-
  script.

## Compared to the docs site

The
[`/lua-api/` section](../lua-api/index.md) on this site is the
same data, rendered to Markdown by `gen_lua_api_docs.py`. The
docks-side cheatsheet is for in-editor lookup; the site is for
cross-referencing from other docs + worked-example mining from
the demo scripts.

If the cheatsheet shows something the site doesn't, the
[CI drift gate](../contributing/ci.md#docsyml-mkdocs-site)
caught a missed regen — the committed
`docs/lua-api/*.md` is stale relative to `luaapi.hh`.

## Related

- [`reference/lua-cheatsheet.md`](../reference/lua-cheatsheet.md)
  — short prose reference for the most-used callables.
- [Lua REPL](lua-repl.md) — fire the methods you just looked up.
