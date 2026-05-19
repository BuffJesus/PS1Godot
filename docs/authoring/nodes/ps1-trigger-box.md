# PS1TriggerBox

World-space axis-aligned trigger volume. Fires Lua callbacks on
player AABB overlap.

<!-- SCREENSHOT: nodes/ps1-trigger-box-inspector.png -->

## Where it goes

Anywhere under a [`PS1Scene`](ps1-scene.md). Place where you want
gameplay-trigger behavior — area-entry music cues, scripted
encounters, teleport portals, save points, etc.

## Key fields

- **Size** — local half-extents in Godot units. Default `(1, 1, 1)`
  gives a 2×2×2 box around the node's transform.
- **TriggerIndex** — integer passed to the Lua callback so a single
  script can serve multiple triggers (a switch statement on the
  index). Defaults to 0.
- **ScriptFile** — `.lua` to dispatch `onTriggerEnter(self, index)`
  + `onTriggerExit(self, index)` against.
- **OneShot** — when true, the trigger fires exactly once per
  scene load and then deactivates.

## Runtime behavior

- **Detection** — every frame the runtime tests the player's AABB
  against every trigger's AABB. Cheap; thousands of triggers per
  scene are fine.
- **Debounce** — `onTriggerExit` fires a few frames after the
  overlap actually ends (handles brushing-the-edge cases without
  enter/exit storms).
- **OneShot semantics** — set `Persist.Set("triggered_N", true)`
  in `onTriggerEnter` for cross-scene OneShot behavior; the node's
  OneShot field is per-scene-load only.

## Lua surface

```lua
function onTriggerEnter(self, index)
    if index == 0 then
        Music.Play("boss_theme")
    elseif index == 1 then
        Scene.Load(3) -- teleport
    end
end

function onTriggerExit(self, index)
    -- typically empty for one-shot triggers
end
```

## Workflows

- **Visualize during authoring** — Godot's gizmo shows the box
  outline. Snap-to-grid is your friend; triggers floating
  off-floor cause "I walked right through it but nothing fired"
  bugs.
- **Music regions** — a trigger sized to the room dimensions
  starts the music on entry; pair with a sibling trigger at the
  doorway to stop it on exit.
- **Save points** — small trigger + a Lua call to
  `Persist.Save()` + a UI confirmation.

## Related

- [Lua API → Persist](../../lua-api/persist.md) for cross-scene
  state.
- [PS1MeshInstance](ps1-mesh-instance.md) with `Collision =
  Trigger` for mesh-shaped triggers.
