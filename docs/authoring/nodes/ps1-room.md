# PS1Room

A convex room volume for the interior portal/room occlusion
system. The runtime walks only rooms reachable through portals
from the camera — the PSX-era trick for indoor scenes too dense
to draw whole.

<!-- SCREENSHOT: nodes/ps1-room-inspector.png -->

## Where it goes

Direct child of [`PS1Scene`](ps1-scene.md). One per logical room:
each dungeon chamber, each house interior, each cave segment.

## Key fields

- **Size** — local half-extents defining the room's AABB.
- **RoomName** — string identifier, used in diagnostics + portal
  authoring.

## Per-triangle assignment

At export time, every triangle in the scene is assigned to the
**room whose AABB contains the majority of its vertices** (ties
broken by centroid distance). You don't tag triangles manually —
the exporter does it based on geometry overlap with room AABBs.

This means:

- **Author rooms first, geometry second** — when you reshape a
  room, triangles re-assign automatically on next export.
- **Watch for cross-room geometry** — a long wall straddling two
  rooms picks one room based on vertex majority; the unpicked
  room sees a missing wall. Split the geometry at the room
  boundary if this matters.

## Independent of nav

Rooms control **what the renderer draws**.
[`PS1NavRegion`](#) (not yet documented here) controls
**where the player can walk**. You usually want one of each per
logical room, sized similarly but tuned independently — nav needs
to match floor reachability, rooms need to match draw-occlusion
intent.

## Without rooms

A scene without any `PS1Room` nodes uses simple frustum culling
and renders everything in view. Add rooms only when you have an
interior dense enough to benefit from portal culling. Outdoor
scenes with one viewable area get nothing useful from rooms.

## Related

- [PS1PortalLink](ps1-portal-link.md) — required companion node.
  A room without portals connecting it to others becomes a closed
  box.
- [`reference/splashpack-format.md`](../../reference/splashpack-format.md)
  — how room + portal data lands on disk.
