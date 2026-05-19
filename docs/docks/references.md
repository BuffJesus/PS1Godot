# PS1 References

Under **PS1 Tools → References**. Cross-project "who uses this
asset?" — paste a resource path, see every `.tscn` / `.tres` /
`.lua` that references it.

<!-- SCREENSHOT: docks/references.png — asset reference list for one of the demo textures -->

## The Find-Usages-for-assets equivalent

Godot's built-in "Find in Files" handles text search; this dock
handles **asset reference** search. The two are different in
practice — a resource's UID (`uid://abc123`) may be referenced
without the string form ever appearing in any file, and a path
(`res://foo/bar.png`) can be referenced from multiple file formats
that each represent it slightly differently.

## What it finds

- **UID references** — `uid="uid://..."` in `.tscn` files,
  `ExtResource("...")` in resource files where the ExtResource's
  UID matches the target.
- **Path-string references** — literal `"res://..."` in `.tres`
  and `.lua`. Catches Lua snippets like
  `Audio.PlaySfx("res://demo/sfx/door.wav")` where the path
  appears verbatim.

Doesn't yet find **name-only** references — a Lua call to
`Audio.PlaySfx("door_creak")` (by clip name, not path) isn't
flagged. Slice 2 plan: layer name-based search via
`PS1Scene.AudioClips`'s name → path map.

## Workflows

- **Manual** — paste a path into the dock's text field, click
  **Find**. List populates.
- **Selection-driven** — when you select an asset in the
  FileSystem dock, References auto-populates with the selected
  asset's path. Useful as a passive "do I still need this
  asset?" check before deleting.

## When to use it

- **Before deleting an asset** — confirm nothing references it.
  Godot will let you delete an asset that has references and
  break those references silently; this dock catches it first.
- **Refactoring asset paths** — moved a texture from
  `res://demo/` to `res://assets/textures/`? Find everything
  that referenced the old path.
- **Asset audit** — see how many places reuse a single texture
  vs how many are one-offs.

## Related

- [Graph Find](graph-find.md) — string content search across
  `.tres` graph resources, not path-based asset references.
- Godot's built-in `Edit → Find in Files` — text search across
  source code (.cs / .gd) for non-asset content.
