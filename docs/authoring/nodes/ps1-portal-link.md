# PS1PortalLink

Connects two [`PS1Room`](ps1-room.md) volumes with a portal quad.
Place at the opening (doorway / archway / window) and point at the
room on the far side.

<!-- SCREENSHOT: nodes/ps1-portal-link-inspector.png -->

## Where it goes

Direct child of [`PS1Scene`](ps1-scene.md). One per opening between
two rooms. A door between Room A and Room B = one PS1PortalLink at
that door.

## Transform interpretation

The portal's `transform` describes the opening:

- **position** — portal center.
- **forward (-Z)** — portal facing direction (its normal).
- **right (+X)** — local +X axis (half of `PortalSize.X` to each
  side).
- **up (+Y)** — local +Y axis (half of `PortalSize.Y` to each
  side).

So the portal is a quad of size `(PortalSize.X, PortalSize.Y)`
centered at `position`, oriented per the rotation.

## Key fields

- **PortalSize** — `Vector2`, width × height in Godot units.
  Default `(2, 2)`.
- **FromRoom** — reference to the [`PS1Room`](ps1-room.md) on the
  near side (the side the portal's `-Z` faces).
- **ToRoom** — the room on the far side.

## Authoring tips

- **Place at the wall opening**, not the door plane — the portal
  is the *void* the player can see through.
- **Slightly larger than the visible opening** — the runtime
  tests the portal quad against the view frustum; a portal
  exactly matching the doorway gets culled when the player looks
  through the doorway at an angle.
- **Mark the visible direction with a label** in the scene tree.
  The transform's `forward` is hidden from glance; "PortalA→B" in
  the node name keeps the topology obvious.

## Two-way portals

A real-world doorway is bidirectional. You can either:

- Place **one** PS1PortalLink with FromRoom + ToRoom; the runtime
  walks portals both ways at render time, or
- Place **two** PS1PortalLinks with opposite FromRoom/ToRoom; lets
  you set asymmetric `PortalSize` per direction (rare).

Default to one.

## Related

- [PS1Room](ps1-room.md) — required companion.
- [`internal/rfc/visibility-culling.md`](https://github.com/BuffJesus/PS1Godot/blob/main/docs/internal/rfc/visibility-culling.md){ target="_blank" }
  — the design rationale (excluded from this site, browsable on GitHub).
